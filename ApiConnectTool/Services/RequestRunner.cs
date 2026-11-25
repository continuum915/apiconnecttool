using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Services;

public sealed class RequestRunner(IRequestExecutionService executionService, IScriptSandbox? sandbox = null) : IRequestRunner
{
    public async Task<RequestRunResult> RunAsync(RequestItem request, RequestExecutionOptions? options = null,
        CancellationToken cancellationToken = default, int scriptTimeoutMilliseconds = 2000)
    {
        try { return await RunCoreAsync(request, options, cancellationToken, scriptTimeoutMilliseconds); }
        finally { options?.ScriptScopes?.Save(); }
    }

    private async Task<RequestRunResult> RunCoreAsync(RequestItem request, RequestExecutionOptions? options,
        CancellationToken cancellationToken, int scriptTimeoutMilliseconds)
    {
        options ??= new();
        var variables = new Dictionary<string, string>((options.ScriptScopes is null ? options.Variables : options.RunVariables) ?? new Dictionary<string, string>());
        var secretNames = new HashSet<string>(options.SecretVariableNames ?? new HashSet<string>());
        var secrets = RequestSecrets.Collect(request, variables, secretNames);
        if (options.ScriptScopes is not null) secrets.AddRange(options.ScriptScopes.SecretValues());
        var scripts = new List<ScriptRunResult>();
        var console = new List<ScriptConsoleLine>();
        var tests = new List<AssertionResult>();
        async Task<ScriptRunResult> RunScriptAsync(string source, RequestExecutionResult? response, ScriptPhase phase)
        {
            var result = await Task.Run(() => (sandbox ?? new ScriptSandbox()).Run(source, request, response,
                variables, secretNames, secrets, scriptTimeoutMilliseconds, cancellationToken, options.ScriptScopes)).ConfigureAwait(false);
            variables = new(result.Variables);
            secrets.AddRange(result.SecretValues);
            scripts.Add(result);
            console.AddRange(result.Console.Select(line => line with { Phase = phase }));
            tests.AddRange(result.Tests);
            if (result.Outcome != ScriptOutcome.Completed)
                tests.Add(new(Guid.NewGuid(), DefaultStrings.Get("Str.Scripts.Error"), false, "", result.ErrorMessage));
            return result;
        }

        if (!string.IsNullOrWhiteSpace(request.PreRequestScript))
        {
            var pre = await RunScriptAsync(request.PreRequestScript, null, ScriptPhase.PreRequest).ConfigureAwait(false);
            if (pre.Outcome != ScriptOutcome.Completed)
                return new(RequestExecutionResult.Failed(cancellationToken.IsCancellationRequested
                    ? RequestExecutionFailureKind.Canceled : RequestExecutionFailureKind.Validation,
                    pre.ErrorMessage ?? DefaultStrings.Get("Str.Scripts.Error"), resolvedSecretValues: secrets),
                    tests, false, variables, secretNames, scripts) { Console = Redacted(console, secrets) };
        }
        var resolved = options.ScriptScopes?.Build(variables);
        if (resolved is not null) secretNames.UnionWith(resolved.SecretNames);
        var preparedOptions = WithVariables(options, resolved?.Variables ?? variables, secretNames);
        var replay = new RequestReplay(request, preparedOptions);
        var response = await executionService.ExecuteAsync(request, preparedOptions, cancellationToken).ConfigureAwait(false);
        secrets.AddRange(response.ResolvedSecretValues);
        if (!response.IsSuccess)
            return new(response, [], false, variables, secretNames, scripts) { Console = Redacted(console, secrets) };
        if (!string.IsNullOrWhiteSpace(request.TestScript)) await RunScriptAsync(request.TestScript, response, ScriptPhase.Test).ConfigureAwait(false);
        tests.AddRange(new AssertionEngine().Evaluate(request.Assertions, response));
        // Include every version of overwritten secrets, including values written by test scripts.
        var redactor = new SecretRedactor();
        tests = tests.Select(test => test with
        {
            Label = redactor.Redact(test.Label, secrets), ActualValue = redactor.Redact(test.ActualValue, secrets),
            FailureMessage = test.FailureMessage is null ? null : redactor.Redact(test.FailureMessage, secrets)
        }).ToList();
        return new(response, tests, true, variables, secretNames, scripts) { Replay = replay, Console = Redacted(console, secrets) };
    }

    /// <summary>Redacts console output again with the run's final secrets, so values a later
    /// script wrote are hidden in earlier lines too.</summary>
    private static IReadOnlyList<ScriptConsoleLine> Redacted(IEnumerable<ScriptConsoleLine> lines, IEnumerable<string> secrets)
    {
        var redactor = new SecretRedactor();
        var known = secrets.ToArray();
        return lines.Select(line => line with { Text = redactor.Redact(line.Text, known) }).ToArray();
    }

    public static RequestExecutionOptions WithVariables(RequestExecutionOptions source,
        IReadOnlyDictionary<string, string> variables, IReadOnlySet<string> secretNames) => new()
    {
        Timeout = source.Timeout, MaxResponseBytes = source.MaxResponseBytes, FollowRedirects = source.FollowRedirects,
        EnvironmentId = source.EnvironmentId, CollectionEnvironmentId = source.CollectionEnvironmentId, ProxyUrl = source.ProxyUrl, IgnoreCertificateErrors = source.IgnoreCertificateErrors,
        Variables = variables, SecretVariableNames = secretNames
    };

}
