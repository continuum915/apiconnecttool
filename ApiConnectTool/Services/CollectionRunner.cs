using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Services;

public sealed class CollectionRunner(IRequestRunner requestRunner, IProductionSendGuard? productionGuard = null,
    Workspace? workspace = null, Action? saveScopes = null, IConsoleLog? consoleLog = null) : ICollectionRunner
{
    Task<CollectionRunReport> ICollectionRunner.RunAsync(RequestCollection collection, CollectionRunConfiguration settings,
        IProgress<CollectionRunProgress>? progress, CancellationToken cancellationToken) =>
        RunAsync(collection, settings.Environment, progress, cancellationToken, settings);

    public async Task<CollectionRunReport> RunAsync(RequestCollection collection, RequestEnvironment? environment = null,
        IProgress<CollectionRunProgress>? progress = null, CancellationToken cancellationToken = default,
        CollectionRunConfiguration? configuration = null)
    {
        if (consoleLog is not null) ConsoleRecorder.RecordRunStarted(consoleLog, collection.Name);
        try
        {
            var report = await RunCoreAsync(collection, environment, progress, cancellationToken, configuration, SynchronizationContext.Current);
            if (consoleLog is not null) ConsoleRecorder.RecordRunFinished(consoleLog, collection.Name, report);
            return report;
        }
        finally { saveScopes?.Invoke(); }
    }

    private async Task<CollectionRunReport> RunCoreAsync(RequestCollection collection, RequestEnvironment? environment,
        IProgress<CollectionRunProgress>? progress, CancellationToken cancellationToken,
        CollectionRunConfiguration? configuration, SynchronizationContext? ownerContext)
    {
        var watch = Stopwatch.StartNew();
        var entries = new List<CollectionRunEntry>();
        CollectionRunReport Report(CollectionRunEndState end) => new(entries.ToArray(), end, watch.Elapsed);
        // Snapshot before awaiting a dialog: a run cannot mutate or race with the saved collection.
        configuration ??= new();
        var requests = RunOrder(collection, configuration.FolderId).Where(request => configuration.IncludedRequestIds?.Contains(request.Id) ?? true).Select(request => {
            var copy = request.CloneForEditing();
            copy.Auth = EffectiveAuthResolver.Resolve(request, collection).Auth.Clone();
            return copy;
        }).ToArray();
        if (configuration.DelayBetweenRequests < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(configuration), "Delay cannot be negative.");
        if (configuration.ScriptTimeoutMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(configuration), "Script timeout must be positive.");
        environment = configuration.Environment ?? environment;
        var collectionEnvironment = configuration.HasCollectionEnvironment ? configuration.CollectionEnvironment : collection.ActiveEnvironment;
        var scopes = VariableScopeBuilder.Build(workspace, collection, environment, collectionEnvironment: collectionEnvironment);
        var baseSecretNames = scopes.SecretNames;
        var rows = configuration.IterationRows ?? [new Dictionary<string, string>()];
        if (cancellationToken.IsCancellationRequested) return Report(CollectionRunEndState.Cancelled);
        try
        {
            var productionTargets = ProductionTarget.Collect(environment, collectionEnvironment, collection.Name);
            if (productionTargets.Count > 0 && (productionGuard is null ||
                !await productionGuard.ConfirmSendAsync(productionTargets).WaitAsync(cancellationToken).ConfigureAwait(false)))
                return Report(CollectionRunEndState.Cancelled);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Report(CollectionRunEndState.Cancelled);
        }

        for (var iteration = 0; iteration < rows.Count; iteration++)
        {
            var variables = new Dictionary<string, string>();
            foreach (var (key, value) in rows[iteration]) variables[key] = value;
            IReadOnlySet<string> secretNames = baseSecretNames;
            for (var requestIndex = 0; requestIndex < requests.Length; requestIndex++)
            {
                if (cancellationToken.IsCancellationRequested) return Report(CollectionRunEndState.Cancelled);
                var request = requests[requestIndex];
                var scriptScopes = new ScriptVariableScopes(workspace, collection, environment, ownerContext: ownerContext,
                    collectionEnvironment: collectionEnvironment);
                RequestRunResult run;
                try
                {
                    run = await requestRunner.RunAsync(request, new() { EnvironmentId = environment?.Id, CollectionEnvironmentId = collectionEnvironment?.Id, Variables = variables, SecretVariableNames = secretNames,
                        ScriptScopes = scriptScopes, RunVariables = variables,
                        Timeout = TimeSpan.FromSeconds(request.TimeoutSeconds), FollowRedirects = request.FollowRedirects,
                        ProxyUrl = request.ProxyUrl, IgnoreCertificateErrors = request.IgnoreCertificateErrors },
                        cancellationToken, configuration.ScriptTimeoutMilliseconds).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return Report(CollectionRunEndState.Cancelled);
                }
                variables = new(run.Variables);
                secretNames = run.SecretVariableNames;
                var secrets = run.Execution.ResolvedSecretValues.Concat(run.Scripts.SelectMany(script => script.SecretValues))
                    .Concat(scriptScopes.SecretValues())
                    .Concat(secretNames.Where(variables.ContainsKey).Select(name => variables[name])).ToArray();
                var redactor = new SecretRedactor();
                string Redact(string text) => redactor.Redact(text, secrets);
                if (consoleLog is not null) ConsoleRecorder.RecordSend(consoleLog, request.Name, run, secrets);
                // Incomplete validation is errored, never passed; the entry keeps the HTTP status separately.
                var error = run.Execution.ErrorMessage ?? run.Scripts.FirstOrDefault(script => script.Outcome != ScriptOutcome.Completed)?.ErrorMessage
                    ?? (run.ValidationIncomplete ? DefaultStrings.Get("Str.Runner.IncompleteValidation") : null);
                var outcome = !run.Execution.IsSuccess || error is not null ? RunEntryOutcome.Errored
                    : run.Tests.Any(test => !test.Passed) ? RunEntryOutcome.Failed : RunEntryOutcome.Passed;
                var preview = ResponsePreview.Create(run.Execution, secrets);
                var (sentUrl, sentHeaders) = ResponsePreview.Request(run.Execution.SentRequest, secrets);
                var details = new RunEntryDetails(run.Execution.SentRequest?.Method ?? request.Method.Method,
                    sentUrl.Length > 0 ? sentUrl : Redact(request.Url), sentHeaders, preview.HeadersText, preview.BodyPreview,
                    preview.BodyWasTruncated, run.Console.Select(line => line with { Text = Redact(line.Text) }).ToArray());
                var entry = new CollectionRunEntry(request.Id, Redact(request.Name), iteration, (int?)run.Execution.StatusCode,
                    run.Execution.Elapsed, run.Tests, outcome, error is null ? null : Redact(error)) { Details = details };
                entries.Add(entry);
                var report = Report(CollectionRunEndState.Completed);
                progress?.Report(new(entry.RequestName, report.RunCount, report.PassedCount, report.FailedCount, report.ErroredCount, entry));
                if (cancellationToken.IsCancellationRequested) return Report(CollectionRunEndState.Cancelled);
                if (configuration.StopOnFailure && outcome != RunEntryOutcome.Passed)
                    return Report(CollectionRunEndState.StoppedOnFailure);
                var hasNext = requestIndex + 1 < requests.Length || iteration + 1 < rows.Count;
                if (hasNext && configuration.DelayBetweenRequests > TimeSpan.Zero)
                {
                    try { await Task.Delay(configuration.DelayBetweenRequests, cancellationToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    { return Report(CollectionRunEndState.Cancelled); }
                }
            }
        }
        return Report(CollectionRunEndState.Completed);
    }

    // The root shows folders then requests; each folder shows requests then child folders.
    /// <summary>The requests a run of <paramref name="collection"/> (or of one folder) sends, in
    /// order: each folder's requests then its subfolders, folders before root requests.</summary>
    public static IEnumerable<RequestItem> RunOrder(RequestCollection collection, Guid? folderId)
    {
        var visited = new HashSet<Guid>();
        IEnumerable<RequestItem> Folder(CollectionFolder folder)
        {
            if (!visited.Add(folder.Id)) yield break;
            foreach (var request in collection.Requests.Where(request => request.FolderId == folder.Id).OrderBy(request => request.SortOrder)) yield return request;
            foreach (var child in collection.Folders.Where(child => child.ParentFolderId == folder.Id).OrderBy(child => child.SortOrder))
                foreach (var request in Folder(child)) yield return request;
        }
        if (folderId is Guid id)
        {
            var selected = collection.Folders.FirstOrDefault(folder => folder.Id == id);
            if (selected is not null) foreach (var request in Folder(selected)) yield return request;
            yield break;
        }
        foreach (var folder in collection.Folders.Where(folder => folder.ParentFolderId is null).OrderBy(folder => folder.SortOrder))
            foreach (var request in Folder(folder)) yield return request;
        foreach (var request in collection.Requests.Where(request => request.FolderId is null).OrderBy(request => request.SortOrder)) yield return request;
    }
}
