using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.Services;

/// <summary>The active collection environment as a resolution scope: precedence against every
/// other scope, row rules within it, and what Send, the inspector, and redaction see.</summary>
public class CollectionEnvironmentPrecedenceTests
{
    private static EnvironmentVariable Row(string key, string value) => new() { Key = key, InitialValue = value };

    /// <summary>Every scope defines "key"; scopes from <paramref name="removedFromTop"/> upward are
    /// dropped, so each case proves one adjacent pair.</summary>
    [Theory]
    [InlineData(0, "run")]
    [InlineData(1, "collection environment")]
    [InlineData(2, "global environment")]
    [InlineData(3, "collection")]
    [InlineData(4, "globals")]
    public void EachScopeBeatsTheOneBelowIt(int removedFromTop, string expected)
    {
        var workspace = new Workspace { GlobalVariables = { Row("key", "globals") } };
        var collection = new RequestCollection { Variables = { Row("key", "collection") } };
        var global = new RequestEnvironment { Name = "qa", Variables = { Row("key", "global environment") } };
        var collectionEnvironment = new RequestEnvironment { Name = "orders qa", Variables = { Row("key", "collection environment") } };
        var run = new Dictionary<string, string> { ["key"] = "run" };

        var map = VariableScopeBuilder.Build(workspace,
            removedFromTop >= 4 ? null : collection,
            removedFromTop >= 3 ? null : global,
            removedFromTop >= 1 ? null : run,
            removedFromTop >= 2 ? null : collectionEnvironment);

        Assert.Equal(expected, map.Variables["key"]);
    }

    [Fact]
    public void CollectionEnvironmentDefinitionsCarryTheirOwnScopeKindAndName()
    {
        var global = new RequestEnvironment { Name = "qa", Variables = { Row("baseUrl", "https://qa.example.test") } };
        var collectionEnvironment = new RequestEnvironment { Name = "orders qa", Variables = { Row("baseUrl", "https://orders-qa.example.test") } };

        var map = VariableScopeBuilder.Build(environment: global, collectionEnvironment: collectionEnvironment);

        var winner = Assert.Single(map.Definitions, definition => definition.IsWinner);
        Assert.Equal(VariableScopeKind.CollectionEnvironment, winner.ScopeKind);
        Assert.Equal("orders qa", winner.ScopeName);
        var shadowed = Assert.Single(map.Definitions, definition => !definition.IsWinner);
        Assert.Equal(VariableScopeKind.Environment, shadowed.ScopeKind);
    }

    [Fact]
    public void DisabledAndKeylessCollectionEnvironmentRowsAreIgnored()
    {
        var global = new RequestEnvironment { Variables = { Row("host", "global") } };
        var collectionEnvironment = new RequestEnvironment { Variables = {
            new() { Key = "host", InitialValue = "disabled", IsEnabled = false },
            new() { Key = " ", InitialValue = "keyless" } } };

        var map = VariableScopeBuilder.Build(environment: global, collectionEnvironment: collectionEnvironment);

        Assert.Equal("global", map.Variables["host"]);
        Assert.False(map.Variables.ContainsKey(" "));
    }

    [Fact]
    public void CollectionEnvironmentCurrentValueBeatsItsInitialValueAndSecretsAreNamed()
    {
        var collectionEnvironment = new RequestEnvironment { Variables = {
            new() { Key = "token", InitialValue = "shared", CurrentValue = "local", HasCurrentValue = true, IsSecret = true } } };

        var map = VariableScopeBuilder.Build(collectionEnvironment: collectionEnvironment);

        Assert.Equal("local", map.Variables["token"]);
        Assert.Contains("token", map.SecretNames);
    }

    [Fact]
    public void WithoutACollectionEnvironmentResolutionIsUnchanged()
    {
        var workspace = new Workspace { GlobalVariables = { Row("a", "globals"), Row("b", "globals") } };
        var collection = new RequestCollection { Variables = { Row("a", "collection") } };
        var global = new RequestEnvironment { Variables = { Row("b", "global environment") } };

        var map = VariableScopeBuilder.Build(workspace, collection, global);

        Assert.Equal(new Dictionary<string, string> { ["a"] = "collection", ["b"] = "global environment" }, map.Variables);
        Assert.DoesNotContain(map.Definitions, definition => definition.ScopeKind == VariableScopeKind.CollectionEnvironment);
    }

    [Fact]
    public void ActiveEnvironmentFollowsTheSelectedIdAndIgnoresUnknownIds()
    {
        var dev = new RequestEnvironment { Name = "dev" };
        var collection = new RequestCollection { Environments = { dev } };
        var changed = new List<string?>();
        collection.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        Assert.Null(collection.ActiveEnvironment);
        collection.ActiveEnvironmentId = dev.Id;
        Assert.Same(dev, collection.ActiveEnvironment);
        Assert.Contains(nameof(RequestCollection.ActiveEnvironment), changed);
        collection.ActiveEnvironmentId = System.Guid.NewGuid();
        Assert.Null(collection.ActiveEnvironment);
    }

    [Fact]
    public void ScriptScopesResolveAndRedactCollectionEnvironmentSecrets()
    {
        var collectionEnvironment = new RequestEnvironment { Variables = { new() { Key = "apiKey", InitialValue = "env-secret", IsSecret = true } } };

        var scopes = new ScriptVariableScopes(null, new RequestCollection(), null, collectionEnvironment: collectionEnvironment);

        Assert.Equal("env-secret", scopes.Build().Variables["apiKey"]);
        Assert.Contains("apiKey", scopes.SecretNames());
        Assert.Contains("env-secret", scopes.SecretValues());
    }

    private static (Workspace Workspace, RequestCollection Orders, RequestCollection Billing) TwoApis()
    {
        var workspace = new Workspace();
        RequestCollection Api(string name)
        {
            var dev = new RequestEnvironment { Name = "dev", Variables = { Row("host", $"https://{name}-dev.example.test") } };
            var qa = new RequestEnvironment { Name = "qa", Variables = { Row("host", $"https://{name}-qa.example.test") } };
            var collection = new RequestCollection { Name = name, Environments = { dev, qa }, ActiveEnvironmentId = dev.Id };
            collection.Requests.Add(new RequestItem { Name = "ping", Url = "{{host}}/ping" });
            workspace.Collections.Add(collection);
            return collection;
        }
        return (workspace, Api("orders"), Api("billing"));
    }

    private static RequestTabViewModel Tab(Workspace workspace, RequestCollection? collection, RequestItem request, List<string> sent,
        RequestEnvironment? global = null)
    {
        var execution = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        {
            sent.Add(message.RequestUri!.GetLeftPart(System.UriPartial.Authority));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        })));
        var tab = RequestTabViewModel.FromRequest(request, execution, () => global);
        tab.WorkspaceProvider = () => workspace;
        tab.CollectionProvider = () => collection;
        return tab;
    }

    [Fact]
    public async Task CollectionsWithDifferentActiveEnvironmentsSendToDifferentHosts()
    {
        var (workspace, orders, billing) = TwoApis();
        var sent = new List<string>();
        var ordersTab = Tab(workspace, orders, orders.Requests[0], sent);
        var billingTab = Tab(workspace, billing, billing.Requests[0], sent);

        orders.ActiveEnvironmentId = orders.Environments.Single(environment => environment.Name == "qa").Id;
        await ordersTab.SendCommand.ExecuteAsync(null);
        await billingTab.SendCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "https://orders-qa.example.test", "https://billing-dev.example.test" }, sent);
        Assert.Null(ordersTab.ResponseActionMessage);
    }

    [Fact]
    public async Task CollectionEnvironmentOverridesTheGlobalEnvironmentOnSend()
    {
        var (workspace, orders, _) = TwoApis();
        var global = new RequestEnvironment { Name = "qa", Variables = { Row("host", "https://shared-qa.example.test"), Row("idp", "https://idp-qa.example.test") } };
        orders.Requests[0].Url = "{{host}}/ping?idp={{idp}}";
        var sent = new List<string>();
        var tab = Tab(workspace, orders, orders.Requests[0], sent, global);

        await tab.SendCommand.ExecuteAsync(null);
        orders.ActiveEnvironmentId = null;
        await tab.SendCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "https://orders-dev.example.test", "https://shared-qa.example.test" }, sent);
    }

    [Fact]
    public async Task DraftTabsWithoutACollectionUseOnlyTheGlobalEnvironment()
    {
        var (workspace, _, _) = TwoApis();
        var global = new RequestEnvironment { Variables = { Row("host", "https://global.example.test") } };
        var sent = new List<string>();
        var tab = Tab(workspace, null, new RequestItem { Url = "{{host}}/ping" }, sent, global);

        await tab.SendCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "https://global.example.test" }, sent);
    }

    [Fact]
    public async Task VariablesOnlyInTheCollectionEnvironmentAreNotReportedUnresolved()
    {
        var (workspace, orders, _) = TwoApis();
        orders.Requests[0].Url = "{{host}}/ping?tenant={{tenant}}";
        orders.ActiveEnvironment!.Variables.Add(Row("tenant", "acme"));
        var tab = Tab(workspace, orders, orders.Requests[0], new List<string>());

        await tab.SendCommand.ExecuteAsync(null);
        Assert.Null(tab.ResponseActionMessage);

        orders.ActiveEnvironmentId = null;
        await tab.SendCommand.ExecuteAsync(null);
        Assert.Contains("{{tenant}}", tab.ResponseActionMessage);
    }

    [Fact]
    public void CurlRedactsCollectionEnvironmentSecrets()
    {
        var (workspace, orders, _) = TwoApis();
        orders.Requests[0].Url = "{{host}}/ping?key={{apiKey}}";
        orders.ActiveEnvironment!.Variables.Add(new() { Key = "apiKey", InitialValue = "orders-dev-secret", IsSecret = true });
        var tab = Tab(workspace, orders, orders.Requests[0], new List<string>());

        Assert.DoesNotContain("orders-dev-secret", tab.BuildCurlCommand(revealSecrets: false));
        Assert.Contains("orders-dev-secret", tab.BuildCurlCommand(revealSecrets: true));
    }

    [Fact]
    public void InspectorShowsTheCollectionEnvironmentAsItsOwnScopeAndNavigatesToIt()
    {
        var (workspace, orders, _) = TwoApis();
        var tab = Tab(workspace, orders, orders.Requests[0], new List<string>());

        var inspector = tab.InspectVariables();
        var navigated = new List<VariableScopeKind>();
        inspector.Navigate += navigated.Add;
        inspector.EnableNavigation();

        var host = Assert.Single(inspector.Variables, variable => variable.Name == "host");
        var expected = string.Format(ApiConnectTool.Services.Localization.AppLocalization.Get("Str.VariableInspector.NamedScope"),
            ApiConnectTool.Services.Localization.AppLocalization.Get("Str.VariableInspector.CollectionEnvironment"), "dev");
        Assert.Equal(expected, host.Source);
        Assert.True(host.EditSourceCommand!.CanExecute(null));
        host.EditSourceCommand.Execute(null);
        Assert.Equal([VariableScopeKind.CollectionEnvironment], navigated);
    }
}
