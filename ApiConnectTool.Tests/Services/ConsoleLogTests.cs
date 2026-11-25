using System.Net;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class ConsoleLogTests
{
    private const string Secret = "s3cr3t-value";

    [Fact]
    public void Log_IsBoundedAndDropsTheOldestEntries()
    {
        var log = new ConsoleLog();
        for (var i = 0; i < ConsoleLog.MaxEntries + 5; i++)
            log.Add(new ConsoleEntry(ConsoleEntryKind.Info, DateTimeOffset.UtcNow, "line " + i));
        var entries = log.Snapshot();
        Assert.Equal(ConsoleLog.MaxEntries, entries.Count);
        Assert.Equal("line 5", entries[0].Text);
        Assert.Equal("line " + (ConsoleLog.MaxEntries + 4), entries[^1].Text);
    }

    private static RequestRunResult Run(RequestExecutionResult execution, IReadOnlyList<AssertionResult>? tests = null,
        IReadOnlyList<ScriptConsoleLine>? console = null) =>
        new(execution, tests ?? [], true, new Dictionary<string, string>(), new HashSet<string>(), []) { Console = console ?? [] };

    [Fact]
    public void RecordSend_RedactsTheUrlHeadersBodyScriptOutputAndFailures()
    {
        var log = new ConsoleLog();
        var execution = new RequestExecutionResult
        {
            IsSuccess = true, StatusCode = HttpStatusCode.OK, Elapsed = TimeSpan.FromMilliseconds(42),
            ContentType = "application/json", Body = System.Text.Encoding.UTF8.GetBytes("{\"token\":\"" + Secret + "\"}"),
            Headers = [new KeyValueItem { Key = "X-Echo", Value = Secret }],
            SentRequest = new SentRequest("POST", new Uri("https://example.test/items?key=" + Secret),
                [new KeyValueItem { Key = "Authorization", Value = "Bearer " + Secret }])
        };
        ConsoleRecorder.RecordSend(log, "Create item", Run(execution,
            [new AssertionResult(Guid.NewGuid(), "token check", false, "", "got " + Secret)],
            [new ScriptConsoleLine(ScriptConsoleLevel.Log, "token " + Secret, ScriptPhase.Test)]), [Secret]);

        var entries = log.Snapshot();
        var network = Assert.Single(entries, entry => entry.Kind == ConsoleEntryKind.Network).Network!;
        Assert.Equal("POST", network.Method);
        Assert.Equal(200, network.StatusCode);
        Assert.Contains("Authorization: Bearer ", network.RequestHeaders);
        Assert.Contains("X-Echo: ", network.ResponseHeaders);
        Assert.Contains("\"token\"", network.BodyPreview);
        var script = Assert.Single(entries, entry => entry.Kind == ConsoleEntryKind.Script);
        Assert.StartsWith("[Create item] test LOG: token ", script.Text);
        Assert.Single(entries, entry => entry.Kind == ConsoleEntryKind.Error);

        foreach (var text in entries.Select(entry => entry.Text)
                     .Concat([network.Url, network.RequestHeaders, network.ResponseHeaders, network.BodyPreview]))
            Assert.DoesNotContain(Secret, text);
    }

    [Fact]
    public void RecordSend_CapsTheBodyPreviewAtFourKilobytes()
    {
        var log = new ConsoleLog();
        ConsoleRecorder.RecordSend(log, "Big", Run(new RequestExecutionResult
        {
            IsSuccess = true, StatusCode = HttpStatusCode.OK, ContentType = "text/plain",
            Body = System.Text.Encoding.UTF8.GetBytes(new string('a', 10_000)),
            SentRequest = new SentRequest("GET", new Uri("https://example.test/big"), [])
        }), []);
        var network = log.Snapshot().Single().Network!;
        Assert.Equal(ConsoleRecorder.MaxBodyPreviewBytes, network.BodyPreview.Length);
        Assert.True(network.BodyTruncated);
    }

    [Fact]
    public void RecordSend_WarnsAboutUnresolvedVariablesAndTruncatedBodies_AndReportsTransportErrors()
    {
        var log = new ConsoleLog();
        ConsoleRecorder.RecordSend(log, "Partial", Run(new RequestExecutionResult
        {
            IsSuccess = true, StatusCode = HttpStatusCode.OK, BodyWasTruncated = true,
            SentRequest = new SentRequest("GET", new Uri("https://example.test/{{missing}}"), [new KeyValueItem { Key = "X-Id", Value = "{{tenant}}" }])
        }), []);
        var warnings = log.Snapshot().Where(entry => entry.Kind == ConsoleEntryKind.Warning).Select(entry => entry.Text).ToArray();
        Assert.Contains(warnings, text => text.Contains("missing") && text.Contains("tenant"));
        Assert.Contains(warnings, text => text.Contains("truncated"));

        log.Clear();
        ConsoleRecorder.RecordSend(log, "Down", Run(RequestExecutionResult.Failed(RequestExecutionFailureKind.Network, "refused " + Secret,
            resolvedSecretValues: [Secret])), [Secret]);
        var error = Assert.Single(log.Snapshot());
        Assert.Equal(ConsoleEntryKind.Error, error.Kind);
        Assert.DoesNotContain(Secret, error.Text);
    }

    [Fact]
    public void RunStart_ClearsUnlessPreserveLogIsOn()
    {
        var log = new ConsoleLog();
        log.Add(new ConsoleEntry(ConsoleEntryKind.Info, DateTimeOffset.UtcNow, "earlier"));
        ConsoleRecorder.RecordRunStarted(log, "Api");
        Assert.Equal(new[] { "Collection run started: Api" }, log.Snapshot().Select(entry => entry.Text));

        log.PreserveLog = true;
        ConsoleRecorder.RecordRunStarted(log, "Api");
        Assert.Equal(2, log.Snapshot().Count);
    }

    [Fact]
    public async Task CollectionRuns_RecordStartEachSendAndTheSummary()
    {
        var log = new ConsoleLog();
        var runner = new CollectionRunner(RequestRunnerTests.Runner(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))),
            consoleLog: log);
        await ((ICollectionRunner)runner).RunAsync(new RequestCollection
        {
            Name = "Api", Requests = { new() { Name = "one", Url = "https://example.test/one" }, new() { Name = "two", Url = "https://example.test/two" } }
        }, new CollectionRunConfiguration());

        var entries = log.Snapshot();
        Assert.Equal(ConsoleEntryKind.Info, entries[0].Kind);
        Assert.Equal(2, entries.Count(entry => entry.Kind == ConsoleEntryKind.Network));
        Assert.StartsWith("Collection run finished: Api. 2 run; 2 passed", entries[^1].Text);
    }

    [Fact]
    public void WindowFiltersCountAndSearch()
    {
        var log = new ConsoleLog();
        log.Add(new ConsoleEntry(ConsoleEntryKind.Info, DateTimeOffset.UtcNow, "started"));
        log.Add(new ConsoleEntry(ConsoleEntryKind.Network, DateTimeOffset.UtcNow, "GET https://a.test",
            new ConsoleNetworkDetails("GET", "https://a.test", 200, TimeSpan.Zero, "", "", "", false)));
        log.Add(new ConsoleEntry(ConsoleEntryKind.Script, DateTimeOffset.UtcNow, "[a] test LOG: hi"));
        log.Add(new ConsoleEntry(ConsoleEntryKind.Warning, DateTimeOffset.UtcNow, "careful"));
        log.Add(new ConsoleEntry(ConsoleEntryKind.Error, DateTimeOffset.UtcNow, "broken"));
        log.Add(new ConsoleEntry(ConsoleEntryKind.Error, DateTimeOffset.UtcNow, "also broken"));
        var vm = new ConsoleWindowViewModel(log);

        Assert.Equal("All (6)", vm.AllText);
        Assert.Equal("Network (1)", vm.NetworkText);
        Assert.Equal("Errors (2)", vm.ErrorsText);
        vm.Filter = ConsoleFilter.Errors;
        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal("2 of 6 shown", vm.ShownText);
        vm.SearchText = "ALSO";
        Assert.Equal("also broken", Assert.Single(vm.Rows).Text);

        log.Clear();
        Assert.Empty(vm.Rows);
        Assert.Equal("All (0)", vm.AllText);
    }
}
