using System.Net;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class ScriptConsoleTests
{
    private static ScriptRunResult Run(string source, IReadOnlySet<string>? secretNames = null,
        IReadOnlyDictionary<string, string>? variables = null) =>
        new ScriptSandbox().Run(source, new RequestItem { Url = "https://example.test" }, null,
            variables ?? new Dictionary<string, string>(), secretNames ?? new HashSet<string>(), []);

    [Fact]
    public void EveryLevel_IsCapturedInOrderWithArgumentsJoined()
    {
        var result = Run("console.log('a', 1, true); console.info({n: 2}); console.warn([1, 2]); console.error(null); console.debug(undefined);");
        Assert.Equal(ScriptOutcome.Completed, result.Outcome);
        Assert.Equal(new[]
        {
            new ScriptConsoleLine(ScriptConsoleLevel.Log, "a 1 true"),
            new ScriptConsoleLine(ScriptConsoleLevel.Info, "{\"n\":2}"),
            new ScriptConsoleLine(ScriptConsoleLevel.Warn, "[1,2]"),
            new ScriptConsoleLine(ScriptConsoleLevel.Error, "null"),
            new ScriptConsoleLine(ScriptConsoleLevel.Debug, "undefined")
        }, result.Console);
    }

    [Fact]
    public void LineLimit_KeepsTwoHundredLinesAndAddsOneTruncationLine()
    {
        var result = Run("for (let i = 0; i < 250; i++) console.log('line ' + i);");
        Assert.Equal(ScriptConsoleLine.MaxLinesPerRun + 1, result.Console.Count);
        Assert.Equal("line 199", result.Console[^2].Text);
        Assert.Equal(ScriptConsoleLevel.Warn, result.Console[^1].Level);
        Assert.Contains("truncated", result.Console[^1].Text);
    }

    [Fact]
    public void CharacterLimit_StopsAtSixteenKibibytes()
    {
        var result = Run("const chunk = 'x'.repeat(4000); for (let i = 0; i < 10; i++) console.log(chunk);");
        Assert.Equal(5, result.Console.Count); // four chunks fit, then the truncation line
        Assert.All(result.Console.Take(4), line => Assert.Equal(4000, line.Text.Length));
        Assert.Contains("truncated", result.Console[^1].Text);
    }

    [Fact]
    public void Output_IsRedactedWithSecretsKnownAtTheEndOfTheRun()
    {
        var result = Run("console.log('token is ' + pm.variables.get('token')); pm.variables.set('token', 'rotated-secret'); console.log('now rotated-secret');",
            new HashSet<string> { "token" }, new Dictionary<string, string> { ["token"] = "original-secret" });
        Assert.DoesNotContain(result.Console, line => line.Text.Contains("original-secret") || line.Text.Contains("rotated-secret"));
        Assert.Equal(2, result.Console.Count);
    }

    [Fact]
    public void OutputBeforeAFailure_IsKept()
    {
        var result = Run("console.log('before'); throw new Error('boom');");
        Assert.Equal(ScriptOutcome.ScriptError, result.Outcome);
        Assert.Equal("before", Assert.Single(result.Console).Text);
    }

    [Fact]
    public void ConsoleCannotBeUsedToReachTheHost()
    {
        var result = Run("pm.test('frozen', () => { let failed = false; try { 'use strict'; console.log = null; } catch (e) { failed = true; } pm.expect(typeof console.log).toEqual('function'); });");
        Assert.True(Assert.Single(result.Tests).Passed);
    }

    [Fact]
    public async Task RequestRunner_ConcatenatesPreRequestThenTestOutput()
    {
        var runner = RequestRunnerTests.Runner(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }));
        var result = await runner.RunAsync(new RequestItem
        {
            Url = "https://example.test",
            PreRequestScript = "console.log('pre'); pm.variables.set('apiKey', 'secret-value');",
            TestScript = "console.warn('test sees ' + pm.variables.get('apiKey'));"
        }, new RequestExecutionOptions { SecretVariableNames = new HashSet<string> { "apiKey" } });

        Assert.Equal(2, result.Console.Count);
        Assert.Equal("pre", result.Console[0].Text);
        Assert.StartsWith("test sees ", result.Console[1].Text);
        Assert.DoesNotContain("secret-value", result.Console[1].Text);
        Assert.Equal(new[] { ScriptPhase.PreRequest, ScriptPhase.Test }, result.Console.Select(line => line.Phase));
        Assert.Equal(ScriptConsoleLevel.Warn, result.Console[1].Level);
    }

    [Fact]
    public async Task RequestRunner_KeepsPreRequestOutputWhenTheScriptFails()
    {
        var runner = RequestRunnerTests.Runner(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var result = await runner.RunAsync(new RequestItem
        {
            Url = "https://example.test",
            PreRequestScript = "console.error('about to fail'); throw new Error('stop');"
        });
        Assert.False(result.Execution.IsSuccess);
        Assert.Equal("about to fail", Assert.Single(result.Console).Text);
    }
}
