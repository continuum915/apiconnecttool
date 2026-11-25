using System.Net;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class CollectionRunnerTests
{
    [Fact]
    public async Task SavedRedirectSettingIsUsedDuringRun()
    {
        var runner = new CollectionRunner(RequestRunnerTests.Runner(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://example.test/next") } })));
        var report = await runner.RunAsync(new() { Requests = new() {
            new() { Url = "https://example.test", FollowRedirects = false } } });
        Assert.Equal(302, Assert.Single(report.Entries).StatusCode);
        Assert.Equal(CollectionRunEndState.Completed, report.EndState);
    }
    [Fact]
    public async Task SidebarOrder_AndTotalsFollowRequestOutcomes()
    {
        var runner = new CollectionRunner(RequestRunnerTests.Runner(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        var parent = new CollectionFolder { Name = "parent" };
        var child = new CollectionFolder { ParentFolderId = parent.Id };
        var collection = new RequestCollection { Folders = new() { parent, child }, Requests = new() {
            new() { Name = "root", Url = "invalid" },
            new() { Name = "child", FolderId = child.Id, Url = "https://example.test" },
            new() { Name = "parent", FolderId = parent.Id, Url = "https://example.test", Assertions = new() { new() { ExpectedValue = "201" } } }
        } };
        var progress = new List<CollectionRunProgress>();
        var report = await runner.RunAsync(collection, progress: new ImmediateProgress<CollectionRunProgress>(progress.Add));
        Assert.Equal(new[] { "parent", "child", "root" }, report.Entries.Select(entry => entry.RequestName));
        Assert.Equal(3, report.RunCount);
        Assert.Equal(1, report.PassedCount);
        Assert.Equal(1, report.FailedCount);
        Assert.Equal(1, report.ErroredCount);
        Assert.Equal(3, progress.Count);
        Assert.Equal(CollectionRunEndState.Completed, report.EndState);
    }
    [Fact]
    public async Task CancellationDuringSend_KeepsEarlierResults()
    {
        using var cancellation = new CancellationTokenSource();
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var runner = new CollectionRunner(RequestRunnerTests.Runner(_ => {
            if (++calls == 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            sending.SetResult();
            return new TaskCompletionSource<HttpResponseMessage>().Task;
        }));
        var running = runner.RunAsync(Collection(3), cancellationToken: cancellation.Token);
        await sending.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        var report = await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(CollectionRunEndState.Cancelled, report.EndState);
        Assert.Equal(2, report.RunCount);
        Assert.Equal(1, report.PassedCount);
        Assert.Equal(RunEntryOutcome.Errored, report.Entries[1].Outcome);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 3)]
    public async Task ProductionGuard_IsConsultedOnceBeforeAnySend(bool proceed, int expectedSends)
    {
        var prompts = 0; var sends = 0;
        var guard = new ProductionSendGuard(() => ProductionWarningMode.AlwaysWarn, _ => { prompts++; return Task.FromResult(proceed); });
        var runner = new CollectionRunner(RequestRunnerTests.Runner(_ => { sends++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); }), guard);
        var report = await runner.RunAsync(Collection(3), new() { IsProduction = true });
        Assert.Equal(1, prompts);
        Assert.Equal(expectedSends, sends);
        Assert.Equal(proceed ? CollectionRunEndState.Completed : CollectionRunEndState.Cancelled, report.EndState);
    }

    [Fact]
    public async Task ScriptVariables_ChainWithinRunAndDoNotChangeEnvironment()
    {
        var environment = new RequestEnvironment { Variables = new() { new() { Key = "token", IsSecret = true, InitialValue = new("original") } } };
        var urls = new List<string>();
        var runner = new CollectionRunner(RequestRunnerTests.Runner(request => { urls.Add(request.RequestUri!.AbsoluteUri); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); }));
        var collection = Collection(2);
        collection.Requests[0].TestScript = "pm.variables.set('token', 'replacement');";
        collection.Requests[1].Url = "https://example.test/{{token}}";
        collection.Requests[1].TestScript = "throw new Error(pm.variables.get('token'));";
        var report = await runner.RunAsync(collection, environment);
        Assert.Equal("https://example.test/replacement", urls[1]);
        Assert.Equal("original", environment.Variables[0].InitialValue.Value);
        Assert.DoesNotContain("replacement", report.Entries[1].ErrorMessage!);
        Assert.Equal(RunEntryOutcome.Errored, report.Entries[1].Outcome);
    }

    [Fact]
    public async Task Iterations_OverlayEnvironmentAndResetScriptWrites()
    {
        var urls = new List<string>();
        var runner = new CollectionRunner(RequestRunnerTests.Runner(request => {
            urls.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }));
        var collection = Collection(2);
        collection.Requests[0].Url = "https://example.test/{{token}}";
        collection.Requests[0].TestScript = "pm.variables.set('token', 'script');";
        collection.Requests[1].Url = "https://example.test/{{token}}";
        var environment = new RequestEnvironment { Variables = new() { new() { Key = "token", InitialValue = new("environment") } } };
        var settings = new CollectionRunConfiguration { IterationRows = [
            new Dictionary<string, string> { ["token"] = "row-one" },
            new Dictionary<string, string> { ["token"] = "row-two" }
        ] };
        var report = await runner.RunAsync(collection, environment, configuration: settings);
        Assert.Equal(new[] { "https://example.test/row-one", "https://example.test/script",
            "https://example.test/row-two", "https://example.test/script" }, urls);
        Assert.Equal(new[] { 0, 0, 1, 1 }, report.Entries.Select(entry => entry.IterationIndex));
        Assert.Equal(4, report.RunCount);
        Assert.Equal("environment", environment.Variables[0].InitialValue.Value);
    }

    [Theory]
    [InlineData(false, 4, CollectionRunEndState.Completed)]
    [InlineData(true, 1, CollectionRunEndState.StoppedOnFailure)]
    public async Task StopOnFailure_HaltsAtFirstFailedEntry(bool stop, int expectedCount, CollectionRunEndState end)
    {
        var collection = Collection(2);
        collection.Requests[0].Assertions = new() { new() { ExpectedValue = "201" } };
        var runner = new CollectionRunner(RequestRunnerTests.Runner(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        var report = await runner.RunAsync(collection, configuration: new() { StopOnFailure = stop,
            IterationRows = [new Dictionary<string, string>(), new Dictionary<string, string>()] });
        Assert.Equal(expectedCount, report.RunCount);
        Assert.Equal(end, report.EndState);
    }

    [Fact]
    public async Task Delay_IsBetweenEntriesAndCancellationInterruptsIt()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new CollectionRunner(RequestRunnerTests.Runner(_ => {
            if (++calls == 1) first.SetResult();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }));
        var running = runner.RunAsync(Collection(2), configuration: new() { DelayBetweenRequests = TimeSpan.FromSeconds(10) },
            cancellationToken: cancellation.Token);
        await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);
        Assert.Equal(1, calls);
        cancellation.Cancel();
        var report = await running.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(CollectionRunEndState.Cancelled, report.EndState);
        Assert.Single(report.Entries);
    }

    [Fact]
    public async Task Delay_IsHonoredBetweenRequests()
    {
        var runner = new CollectionRunner(RequestRunnerTests.Runner(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var report = await runner.RunAsync(Collection(2), configuration: new() { DelayBetweenRequests = TimeSpan.FromMilliseconds(80) });
        Assert.Equal(2, report.RunCount);
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(65));
    }

    [Fact]
    public async Task StopOnFailure_HaltsOnErroredEntry()
    {
        var collection = Collection(2);
        collection.Requests[0].Url = "invalid";
        var runner = new CollectionRunner(RequestRunnerTests.Runner(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        var report = await runner.RunAsync(collection, configuration: new() { StopOnFailure = true });
        Assert.Single(report.Entries);
        Assert.Equal(RunEntryOutcome.Errored, report.Entries[0].Outcome);
        Assert.Equal(CollectionRunEndState.StoppedOnFailure, report.EndState);
    }

    [Fact]
    public async Task Configuration_SelectsEnvironmentAndRequests()
    {
        var urls = new List<string>();
        var runner = new CollectionRunner(RequestRunnerTests.Runner(request => {
            urls.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }));
        var collection = Collection(2);
        collection.Requests[0].Url = "https://example.test/first";
        collection.Requests[1].Url = "{{host}}/second";
        var environment = new RequestEnvironment { Variables = new() { new() { Key = "host", InitialValue = new("https://example.test") } } };
        ICollectionRunner service = runner;
        var report = await service.RunAsync(collection, new() { Environment = environment,
            IncludedRequestIds = new HashSet<Guid> { collection.Requests[1].Id } });
        Assert.Single(report.Entries);
        Assert.Equal(new[] { "https://example.test/second" }, urls);
    }


    [Fact]
    public async Task CancellationWhileAwaitingProductionGuard_ReturnsWithoutSending()
    {
        using var cancellation = new CancellationTokenSource();
        var prompted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var guard = new ProductionSendGuard(() => ProductionWarningMode.AlwaysWarn, _ => {
            prompted.SetResult(); return new TaskCompletionSource<bool>().Task;
        });
        var runner = new CollectionRunner(RequestRunnerTests.Runner(_ => throw new InvalidOperationException("must not send")), guard);
        var running = runner.RunAsync(Collection(1), new() { IsProduction = true }, cancellationToken: cancellation.Token);
        await prompted.Task;
        cancellation.Cancel();
        var result = await running.WaitAsync(TimeSpan.FromMilliseconds(500));
        Assert.Equal(CollectionRunEndState.Cancelled, result.EndState);
        Assert.Empty(result.Entries);
    }

    internal static RequestCollection Collection(int count) => new() { Requests = new(Enumerable.Range(0, count)
        .Select(index => new RequestItem { Name = "request " + index, Url = "https://example.test", SortOrder = index })) };

}

internal sealed class ImmediateProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
