using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class ScriptSandboxTests
{
    [Fact]
    public void NamedTests_ContinueAfterFailure()
    {
        var result = new ScriptSandbox().Run("pm.test('bad', () => pm.expect(1).toEqual(2)); pm.test('good', () => pm.expect(3).toBeGreaterThan(2));",
            new RequestItem(), null, new Dictionary<string, string>(), new HashSet<string>(), []);
        Assert.Equal(ScriptOutcome.Completed, result.Outcome);
        Assert.Equal(new[] { false, true }, result.Tests.Select(test => test.Passed));
    }

    [Theory]
    [InlineData("typeof System", "undefined")]
    [InlineData("typeof importNamespace", "undefined")]
    [InlineData("typeof clrHelper", "undefined")]
    [InlineData("typeof require", "undefined")]
    [InlineData("typeof fetch", "undefined")]
    [InlineData("typeof process", "undefined")]
    [InlineData("typeof pm.request.GetType", "undefined")]
    [InlineData("typeof pm.sendRequest", "undefined")]
    public void HostCapabilities_AreUnreachable(string expression, string expected)
    {
        var run = Run($"pm.test('contained', () => pm.expect({expression}).toEqual('{expected}')); ");
        Assert.True(Assert.Single(run.Tests).Passed);
    }

    [Theory]
    [InlineData("while(true) {}", ScriptOutcome.LimitExceeded)]
    [InlineData("function recurse() { recurse(); } recurse();", ScriptOutcome.LimitExceeded)]
    [InlineData("let a = []; while(true) { a.push('x'.repeat(1024 * 1024)); }", ScriptOutcome.LimitExceeded)]
    [InlineData("let a = 0; for(let i=0; i<60000; i++) a++;", ScriptOutcome.LimitExceeded)]
    [InlineData("throw new Error('bad script');", ScriptOutcome.ScriptError)]
    public void RunawayScripts_ReturnContainedOutcomes(string source, ScriptOutcome expected)
    {
        Assert.Equal(expected, Run(source).Outcome);
    }

    [Fact]
    public void Deadline_IsContained()
    {
        var result = Run("while(true) {}", timeout: 1);
        Assert.Equal(ScriptOutcome.TimedOut, result.Outcome);
    }

    [Fact]
    public void ResponseJsonError_IsCatchableAndRequestIsReadOnly()
    {
        var result = Run("pm.request.url = 'changed'; pm.test('read only', () => pm.expect(pm.request.url).toEqual('original')); pm.test('json error', () => { let caught = false; try { pm.response.json(); } catch(e) { caught = true; } pm.expect(caught).toEqual(true); });",
            new() { IsSuccess = true, Body = System.Text.Encoding.UTF8.GetBytes("plain") });
        Assert.All(result.Tests, test => Assert.True(test.Passed));
    }

    private static ScriptRunResult Run(string source, RequestExecutionResult? response = null, int timeout = 2000) =>
        new ScriptSandbox().Run(source, new() { Url = "original" }, response,
            new Dictionary<string, string>(), new HashSet<string>(), [], timeout);
}
