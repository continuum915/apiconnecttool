using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class ScriptScopeTests
{
    [Theory]
    [InlineData("environment")]
    [InlineData("collectionVariables")]
    [InlineData("globals")]
    public async Task DisabledSecretWritesAreRedactedAndEmptyValuesStayEmpty(string scope)
    {
        var variable = new EnvironmentVariable { Key = "key", InitialValue = "old-private", IsEnabled = false, IsSecret = true };
        var workspace = new Workspace(); var collection = new RequestCollection(); var environment = new RequestEnvironment();
        var rows = scope == "environment" ? environment.Variables : scope == "globals" ? workspace.GlobalVariables : collection.Variables;
        rows.Add(variable);
        var runner = RequestRunnerTests.Runner(_ => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)));
        var options = new RequestExecutionOptions { ScriptScopes = new(workspace, collection, environment) };
        var result = await runner.RunAsync(new() { Url = "https://example.test", TestScript =
            $"pm.{scope}.set('key','fresh-private'); throw new Error('fresh-private');" }, options);
        Assert.DoesNotContain("fresh-private", Assert.Single(result.Scripts).ErrorMessage!);
        Assert.True(variable.IsSecret);
        var empty = await runner.RunAsync(new() { Url = "https://example.test", TestScript = $"pm.{scope}.set('key','');" }, options);
        Assert.Equal(ScriptOutcome.Completed, Assert.Single(empty.Scripts).Outcome);
        Assert.Equal("", options.ScriptScopes.Build().Variables["key"]);
    }
    [Theory]
    [InlineData("environment")]
    [InlineData("collectionVariables")]
    public async Task MissingScopeWriteFailsClearly(string scope)
    {
        var result = await RequestRunnerTests.Runner(_ => throw new Exception("Must not send")).RunAsync(
            new() { Url = "https://example.test", PreRequestScript = $"pm.{scope}.set('key','value');" },
            new() { ScriptScopes = new(new Workspace(), null, null) });
        Assert.False(result.Execution.IsSuccess);
        Assert.Contains(scope + " scope is unavailable", result.Execution.ErrorMessage);
    }

    [Fact]
    public async Task SingleSendSupportsAllScopeMethodsInBothScriptsAndSaves()
    {
        var workspace = new Workspace(); var collection = new RequestCollection(); var environment = new RequestEnvironment();
        var saves = 0;
        var execution = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)))));
        var request = new RequestItem { Url = "https://example.test/{{value}}", PreRequestScript = """
            for (const scope of [pm.globals, pm.collectionVariables, pm.environment]) {
                scope.set('value', 'scoped'); pm.test('has', () => pm.expect(scope.has('value')).toEqual(true));
                pm.test('get', () => pm.expect(scope.get('value')).toEqual('scoped'));
            }
            pm.variables.set('value', 'run'); pm.test('precedence', () => pm.expect(pm.variables.get('value')).toEqual('run'));
            """, TestScript = """
            for (const scope of [pm.globals, pm.collectionVariables, pm.environment]) {
                scope.unset('value'); pm.test('unset', () => pm.expect(scope.has('value')).toEqual(false));
                scope.set('saved', 'current');
            }
            """ };
        var tab = ApiConnectTool.ViewModels.RequestTabViewModel.FromRequest(request, execution, () => environment);
        tab.WorkspaceProvider = () => workspace; tab.CollectionProvider = () => collection; tab.SaveScopes = () => saves++;
        await tab.SendCommand.ExecuteAsync(null);
        Assert.True(tab.LastSendSucceeded);
        Assert.Equal(1, saves);
        Assert.Equal("current", Assert.Single(workspace.GlobalVariables).CurrentValue.Value);
        Assert.Equal("current", Assert.Single(collection.Variables).CurrentValue.Value);
        Assert.Equal("current", Assert.Single(environment.Variables).CurrentValue.Value);
    }
    [Fact]
    public async Task LoginUpdatesEnvironmentAndNextRequestUsesSecretToken()
    {
        var urls = new List<string>();
        var execution = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(request => {
            urls.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        })));
        var environment = new RequestEnvironment { Variables = new() { new() { Key = "token", InitialValue = "old", IsSecret = true } } };
        var workspace = new Workspace();
        var collection = new RequestCollection { Requests = new() {
            new() { Url = "https://example.test/login", TestScript = "pm.environment.set('token','login-secret'); pm.globals.set('g','global'); pm.collectionVariables.set('c','collection');" },
            new() { Url = "https://example.test/{{token}}", PreRequestScript = "pm.test('scopes', () => { pm.expect(pm.globals.get('g')).toEqual('global'); pm.expect(pm.collectionVariables.has('c')).toEqual(true); });",
                TestScript = "throw new Error(pm.environment.get('token'));" } } };
        var report = await new CollectionRunner(new RequestRunner(execution), workspace: workspace).RunAsync(collection, environment);
        Assert.Equal("https://example.test/login-secret", urls[1]);
        Assert.Equal("login-secret", environment.Variables[0].CurrentValue.Value);
        Assert.True(environment.Variables[0].IsSecret);
        Assert.DoesNotContain("login-secret", report.Entries[1].ErrorMessage!);
        Assert.True(report.Entries[1].Tests[0].Passed);
    }
}
