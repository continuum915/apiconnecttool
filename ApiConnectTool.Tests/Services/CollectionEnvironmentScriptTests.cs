using System.Net;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.Services;

/// <summary><c>pm.environment</c> reads the active collection environment before the active global
/// environment and writes to the collection environment when one is selected.</summary>
public sealed class CollectionEnvironmentScriptTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"apiconnecttool-collection-env-scripts-{Guid.NewGuid():N}");
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }

    private static EnvironmentVariable Row(string key, string value) => new() { Key = key, InitialValue = value };

    private static (Workspace Workspace, RequestCollection Collection, RequestEnvironment Global, RequestEnvironment Dev, RequestEnvironment Qa) Setup()
    {
        var global = new RequestEnvironment { Name = "shared", Variables = { Row("host", "global-host"), Row("idp", "global-idp") } };
        var dev = new RequestEnvironment { Name = "dev", Variables = { Row("host", "dev-host") } };
        var qa = new RequestEnvironment { Name = "qa", Variables = { Row("host", "qa-host") } };
        var collection = new RequestCollection { Name = "Orders", Environments = { dev, qa }, ActiveEnvironmentId = dev.Id,
            Variables = { Row("host", "collection-host") } };
        var workspace = new Workspace { Environments = { global }, SelectedEnvironmentId = global.Id, Collections = { collection } };
        return (workspace, collection, global, dev, qa);
    }

    private static Task<RequestRunResult> RunScript(string script, ScriptVariableScopes scopes) =>
        RequestRunnerTests.Runner(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))
            .RunAsync(new() { Url = "https://example.test", PreRequestScript = script }, new() { ScriptScopes = scopes });

    private static string? Current(RequestEnvironment environment, string key) =>
        environment.Variables.LastOrDefault(row => row.Key == key) is { HasCurrentValue: true } row ? row.CurrentValue.Value : null;

    [Fact]
    public async Task GetReadsTheCollectionEnvironmentFirstThenTheGlobalEnvironment()
    {
        var (workspace, collection, global, dev, _) = Setup();

        var run = await RunScript("""
            pm.test('host', () => pm.expect(pm.environment.get('host')).toEqual('dev-host'));
            pm.test('idp', () => pm.expect(pm.environment.get('idp')).toEqual('global-idp'));
            pm.test('has', () => pm.expect(pm.environment.has('idp')).toEqual(true));
            pm.test('variables', () => pm.expect(pm.variables.get('host')).toEqual('dev-host'));
            """, new(workspace, collection, global, collectionEnvironment: dev));

        Assert.All(run.Tests, test => Assert.True(test.Passed, test.FailureMessage));
        Assert.Equal(4, run.Tests.Count);
    }

    [Fact]
    public async Task WithoutACollectionEnvironmentGetReadsTheGlobalEnvironment()
    {
        var (workspace, collection, global, _, _) = Setup();

        var run = await RunScript("pm.test('host', () => pm.expect(pm.environment.get('host')).toEqual('global-host'));",
            new(workspace, collection, global));

        Assert.True(Assert.Single(run.Tests).Passed);
    }

    [Fact]
    public async Task OnlyACollectionEnvironmentIsEnoughForReadsAndWrites()
    {
        var (workspace, collection, _, dev, _) = Setup();

        var run = await RunScript("""
            pm.test('host', () => pm.expect(pm.environment.get('host')).toEqual('dev-host'));
            pm.environment.set('token', 'scripted');
            """, new(workspace, collection, null, collectionEnvironment: dev));

        Assert.True(Assert.Single(run.Tests).Passed);
        Assert.Equal("scripted", Current(dev, "token"));
    }

    [Fact]
    public async Task SetAndUnsetWriteToTheCollectionEnvironmentAndLeaveTheGlobalEnvironmentAlone()
    {
        var (workspace, collection, global, dev, qa) = Setup();
        var scopes = new ScriptVariableScopes(workspace, collection, global, collectionEnvironment: dev);

        var run = await RunScript("""
            pm.environment.set('token', 'orders-token');
            pm.environment.set('idp', 'orders-idp');
            pm.environment.unset('host');
            pm.test('shadow', () => pm.expect(pm.environment.get('idp')).toEqual('orders-idp'));
            """, scopes);

        Assert.True(Assert.Single(run.Tests).Passed);
        Assert.Equal("orders-token", Current(dev, "token"));
        Assert.Equal("orders-idp", Current(dev, "idp"));
        Assert.DoesNotContain(dev.Variables, row => row.Key == "host");
        Assert.Equal(new[] { "host", "idp" }, global.Variables.Select(row => row.Key));
        Assert.All(global.Variables, row => Assert.False(row.HasCurrentValue));
        Assert.Equal(new[] { "host" }, qa.Variables.Select(row => row.Key));
        Assert.Equal("global-host", scopes.Build().Variables["host"]);
    }

    [Fact]
    public async Task WithoutACollectionEnvironmentWritesGoToTheGlobalEnvironment()
    {
        var (workspace, collection, global, dev, _) = Setup();

        await RunScript("pm.environment.set('token', 'shared-token'); pm.environment.unset('idp');", new(workspace, collection, global));

        Assert.Equal("shared-token", Current(global, "token"));
        Assert.DoesNotContain(global.Variables, row => row.Key == "idp");
        Assert.DoesNotContain(dev.Variables, row => row.Key == "token");
    }

    [Fact]
    public async Task CollectionVariablesAndGlobalsAreUnchanged()
    {
        var (workspace, collection, global, dev, _) = Setup();

        var run = await RunScript("""
            pm.test('collection', () => pm.expect(pm.collectionVariables.get('host')).toEqual('collection-host'));
            pm.collectionVariables.set('c', 'collection-value');
            pm.globals.set('g', 'globals-value');
            """, new(workspace, collection, global, collectionEnvironment: dev));

        Assert.True(Assert.Single(run.Tests).Passed);
        Assert.Contains(collection.Variables, row => row.Key == "c" && row.CurrentValue.Value == "collection-value");
        Assert.Contains(workspace.GlobalVariables, row => row.Key == "g" && row.CurrentValue.Value == "globals-value");
        Assert.DoesNotContain(dev.Variables, row => row.Key is "c" or "g");
    }

    [Fact]
    public async Task ScriptedCollectionEnvironmentValuesAreSavedWithTheWorkspaceAndUsedByTheNextSend()
    {
        var service = new WorkspaceService(_directory);
        var (workspace, collection, global, dev, _) = Setup();
        service.SaveWorkspace(workspace);
        workspace = service.LoadWorkspace();
        collection = workspace.Collections.Single();
        global = workspace.Environments.Single();
        var urls = new List<string>();
        var execution = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        {
            urls.Add(message.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        })));
        var login = new RequestItem { Url = "https://example.test/login", TestScript = "pm.environment.set('token', 'orders-token');" };
        var next = new RequestItem { Url = "https://example.test/data?token={{token}}" };
        collection.Requests.Add(login);
        collection.Requests.Add(next);
        RequestTabViewModel Tab(RequestItem request)
        {
            var tab = RequestTabViewModel.FromRequest(request, execution, () => global);
            tab.WorkspaceProvider = () => workspace;
            tab.CollectionProvider = () => collection;
            tab.SaveScopes = () => service.SaveWorkspace(workspace);
            return tab;
        }

        await Tab(login).SendCommand.ExecuteAsync(null);
        await Tab(next).SendCommand.ExecuteAsync(null);

        Assert.Equal("https://example.test/data?token=orders-token", urls.Last());
        var reloaded = service.LoadWorkspace();
        var reloadedDev = reloaded.Collections.Single().Environments.Single(environment => environment.Id == dev.Id);
        Assert.Equal("orders-token", Current(reloadedDev, "token"));
        Assert.DoesNotContain(reloaded.Environments.Single().Variables, row => row.Key == "token");
    }

    [Fact]
    public async Task CollectionRunnerScriptsWriteToTheRunsCollectionEnvironment()
    {
        var (workspace, collection, global, dev, qa) = Setup();
        collection.Requests.Add(new RequestItem { Name = "login", Url = "https://example.test/login", TestScript = "pm.environment.set('token', 'run-token');" });
        var saves = 0;
        ICollectionRunner runner = new CollectionRunner(RequestRunnerTests.Runner(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))),
            workspace: workspace, saveScopes: () => saves++);

        await runner.RunAsync(collection, new() { Environment = global, CollectionEnvironment = qa }, null, default);

        Assert.Equal("run-token", Current(qa, "token"));
        Assert.DoesNotContain(dev.Variables, row => row.Key == "token");
        Assert.DoesNotContain(global.Variables, row => row.Key == "token");
        Assert.Equal(dev.Id, collection.ActiveEnvironmentId);
        Assert.True(saves > 0);
    }
}
