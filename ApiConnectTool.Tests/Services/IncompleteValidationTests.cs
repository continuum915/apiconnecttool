using System.Net;
using System.Text;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

// Acceptance-matrix finding 10: body-dependent validation never passes on truncated preview bytes.
public class IncompleteValidationTests
{
    private const string FullBody = "ok-bad-suffix";

    private static RequestRunner Runner(string body = FullBody) => RequestRunnerTests.Runner(_ => Task.FromResult(
        new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/plain") }));

    private static RequestExecutionResult Truncated(string preview) => new()
    {
        IsSuccess = true, StatusCode = HttpStatusCode.OK, BodyWasTruncated = true, Elapsed = TimeSpan.FromMilliseconds(12.5),
        Body = Encoding.UTF8.GetBytes(preview), Headers = [new() { Key = "Content-Type", Value = "application/json" }]
    };

    [Fact]
    public async Task CappedPrefix_CannotPassBodyEquals()
    {
        var run = await Runner().RunAsync(new() { Url = "https://example.test", Assertions = new() {
            new() { Kind = AssertionKind.Body, Operator = AssertionOperator.Equals, ExpectedValue = "ok" },
            new() { Description = "status" } } }, new() { MaxResponseBytes = 2 });

        Assert.True(run.Execution.IsSuccess);
        Assert.True(run.Execution.BodyWasTruncated);
        var body = run.Tests[0];
        Assert.False(body.Passed);
        Assert.True(body.Incomplete);
        Assert.Contains("truncated", body.FailureMessage!);
        Assert.Equal("", body.ActualValue);
        Assert.True(run.Tests[1].Passed);
        Assert.False(run.Tests[1].Incomplete);
        Assert.True(run.ValidationIncomplete);
    }

    [Fact]
    public async Task CompleteBody_KeepsExistingBodyValidation()
    {
        var run = await Runner("ok").RunAsync(new() { Url = "https://example.test", Assertions = new() {
            new() { Kind = AssertionKind.Body, Operator = AssertionOperator.Equals, ExpectedValue = "ok" } } });
        var result = Assert.Single(run.Tests);
        Assert.True(result.Passed);
        Assert.False(result.Incomplete);
        Assert.False(run.ValidationIncomplete);
    }

    [Theory]
    [InlineData(AssertionKind.Body, "", AssertionOperator.Contains, "id")]
    [InlineData(AssertionKind.JsonPath, "$.id", AssertionOperator.Exists, "")]
    [InlineData(AssertionKind.Schema, "{\"type\":\"object\"}", AssertionOperator.Equals, "")]
    public void BodyDependentAssertions_ReportIncompleteOnTruncatedBody(AssertionKind kind, string target, AssertionOperator op, string expected)
    {
        var result = Assert.Single(new AssertionEngine().Evaluate(
            [new() { Kind = kind, Target = target, Operator = op, ExpectedValue = expected }], Truncated("{\"id\":1")));
        Assert.False(result.Passed);
        Assert.True(result.Incomplete);
        Assert.Contains("truncated", result.FailureMessage!);
    }

    [Fact]
    public void MetadataAssertions_StillEvaluateOnTruncatedBody()
    {
        var results = new AssertionEngine().Evaluate([
            new() { Kind = AssertionKind.StatusCode, ExpectedValue = "200" },
            new() { Kind = AssertionKind.Header, Target = "content-type", Operator = AssertionOperator.Contains, ExpectedValue = "json" },
            new() { Kind = AssertionKind.ResponseTime, Operator = AssertionOperator.LessThan, ExpectedValue = "100" },
            new() { Kind = AssertionKind.StatusCode, ExpectedValue = "201" },
            new() { Kind = AssertionKind.Body, IsEnabled = false }], Truncated("{"));
        Assert.Equal(new[] { true, true, true, false }, results.Select(result => result.Passed));
        Assert.All(results, result => Assert.False(result.Incomplete));
    }

    [Theory]
    [InlineData("pm.response.text()")]
    [InlineData("pm.response.json()")]
    [InlineData("pm.response.body")]
    [InlineData("Object.getOwnPropertyDescriptor(pm.response, 'body').get()")]
    public void Scripts_CannotReadTruncatedBodyEvenWhenErrorIsCaught(string access)
    {
        var run = new ScriptSandbox().Run($"let leaked = 'none'; try {{ leaked = {access}; }} catch (e) {{ }} pm.test('done', () => pm.expect(leaked).toEqual('none'));",
            new RequestItem(), Truncated("{\"id\""), new Dictionary<string, string>(), new HashSet<string>(), []);
        Assert.Equal(ScriptOutcome.Completed, run.Outcome);
        Assert.Contains(run.Tests, test => test.Label == "done" && test.Passed);
        var incomplete = Assert.Single(run.Tests, test => test.Incomplete);
        Assert.False(incomplete.Passed);
    }

    [Fact]
    public void Scripts_TestThatReadsTruncatedBodyIsIncomplete()
    {
        var run = new ScriptSandbox().Run("pm.test('starts', () => { try { pm.response.text(); } catch (e) { } });",
            new RequestItem(), Truncated("ok"), new Dictionary<string, string>(), new HashSet<string>(), []);
        // The read is reported once, on the test that made it.
        var test = Assert.Single(run.Tests);
        Assert.Equal("starts", test.Label);
        Assert.False(test.Passed);
        Assert.True(test.Incomplete);
    }

    [Fact]
    public void Scripts_MetadataOnlyRemainUsableAndSeeTruncationFlag()
    {
        var run = new ScriptSandbox().Run("""
            pm.test('flag', () => pm.expect(pm.response.bodyTruncated).toEqual(true));
            pm.test('status', () => pm.expect(pm.response.status).toEqual(200));
            pm.test('header', () => pm.expect(pm.response.headers['Content-Type']).toEqual('application/json'));
            pm.test('time', () => pm.expect(pm.response.responseTime).toBeLessThan(100));
            pm.test('logged', () => pm.expect(JSON.stringify({...pm.response}).includes('"bodyTruncated":true')).toEqual(true));
            """, new RequestItem(), Truncated("{"), new Dictionary<string, string>(), new HashSet<string>(), []);
        Assert.Equal(ScriptOutcome.Completed, run.Outcome);
        Assert.All(run.Tests, test => Assert.True(test.Passed, test.Label + ": " + test.FailureMessage));
        Assert.Equal(5, run.Tests.Count);
    }

    [Fact]
    public void Scripts_CompleteBodyKeepsTextJsonAndBody()
    {
        var response = Truncated("{\"id\":7}");
        response = new()
        {
            IsSuccess = true, StatusCode = HttpStatusCode.OK, Body = response.Body, Headers = response.Headers
        };
        var run = new ScriptSandbox().Run("""
            pm.test('flag', () => pm.expect(pm.response.bodyTruncated).toEqual(false));
            pm.test('json', () => pm.expect(pm.response.json().id).toEqual(7));
            pm.test('text', () => pm.expect(pm.response.text()).toEqual(pm.response.body));
            pm.test('logged', () => pm.expect(JSON.parse(JSON.stringify(pm.response)).body).toEqual('{"id":7}'));
            """, new RequestItem(), response, new Dictionary<string, string>(), new HashSet<string>(), []);
        Assert.Equal(4, run.Tests.Count);
        Assert.All(run.Tests, test => Assert.True(test.Passed, test.Label + ": " + test.FailureMessage));
    }

    [Fact]
    public async Task UncaughtScriptBodyAccess_IsIncompleteThroughRunner()
    {
        var run = await Runner().RunAsync(new() { Url = "https://example.test", TestScript = "pm.response.text();" },
            new() { MaxResponseBytes = 2 });
        Assert.True(run.Execution.IsSuccess);
        Assert.True(run.ValidationIncomplete);
        Assert.Contains(run.Tests, test => test.Incomplete);
    }

    [Theory]
    [InlineData(false, 2, CollectionRunEndState.Completed)]
    [InlineData(true, 1, CollectionRunEndState.StoppedOnFailure)]
    public async Task CollectionRun_ClassifiesIncompleteValidationAsErrored(bool stop, int expectedCount, CollectionRunEndState end)
    {
        var large = new byte[RequestExecutionOptions.DefaultMaxResponseBytes + 16];
        var runner = new CollectionRunner(RequestRunnerTests.Runner(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(large) })));
        var collection = new RequestCollection { Requests = new() {
            new() { Name = "first", Url = "https://example.test/1", SortOrder = 0, Assertions = new() {
                new() { Kind = AssertionKind.Body, Operator = AssertionOperator.Contains, ExpectedValue = "x" },
                new() { ExpectedValue = "200" } } },
            new() { Name = "second", Url = "https://example.test/2", SortOrder = 1 } } };

        var report = await runner.RunAsync(collection, configuration: new() { StopOnFailure = stop });

        Assert.Equal(expectedCount, report.RunCount);
        Assert.Equal(end, report.EndState);
        var entry = report.Entries[0];
        Assert.Equal(RunEntryOutcome.Errored, entry.Outcome);
        Assert.Equal(200, entry.StatusCode);
        Assert.Contains("truncated", entry.ErrorMessage!);
        Assert.True(entry.Tests[1].Passed);
        Assert.Contains("1 incomplete", entry.TestsSummary);
    }
}
