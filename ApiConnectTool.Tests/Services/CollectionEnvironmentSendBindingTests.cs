using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.ViewModels;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.Services;

/// <summary>The collection runner, folder runs, moved requests, and OAuth token binding resolve
/// with the owning collection's active environment, the same way Send does.</summary>
public class CollectionEnvironmentSendBindingTests
{
    private static EnvironmentVariable Row(string key, string value) => new() { Key = key, InitialValue = value };

    private sealed class Api
    {
        public required Workspace Workspace { get; init; }
        public required RequestCollection Orders { get; init; }
        public required RequestCollection Billing { get; init; }
        public required RequestEnvironment Global { get; init; }
        public required RequestEnvironment OtherGlobal { get; init; }
        public RequestEnvironment Env(RequestCollection collection, string name) => collection.Environments.Single(environment => environment.Name == name);
    }

    private static Api TwoApis()
    {
        var global = new RequestEnvironment { Name = "shared qa", Variables = { Row("idp", "idp-qa"), Row("host", "https://shared-qa.example.test") } };
        var otherGlobal = new RequestEnvironment { Name = "shared dev", Variables = { Row("idp", "idp-dev"), Row("host", "https://shared-dev.example.test") } };
        var workspace = new Workspace { Environments = { global, otherGlobal }, SelectedEnvironmentId = global.Id };
        RequestCollection Collection(string name)
        {
            var dev = new RequestEnvironment { Name = "dev", Variables = { Row("host", $"https://{name}-dev.example.test") } };
            var qa = new RequestEnvironment { Name = "qa", Variables = { Row("host", $"https://{name}-qa.example.test") } };
            var collection = new RequestCollection { Name = name, Environments = { dev, qa }, ActiveEnvironmentId = dev.Id };
            collection.Requests.Add(new RequestItem { Name = "ping", Url = "{{host}}/ping?idp={{idp}}" });
            workspace.Collections.Add(collection);
            return collection;
        }
        return new() { Workspace = workspace, Orders = Collection("orders"), Billing = Collection("billing"), Global = global, OtherGlobal = otherGlobal };
    }

    private static MainWindowViewModel CreateMain(Api api, List<string> sent, IOAuthTokenService? oauth = null, Func<HttpRequestMessage, HttpResponseMessage?>? respond = null)
    {
        var factory = new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        {
            if (respond?.Invoke(message) is { } response) return Task.FromResult(response);
            sent.Add(message.RequestUri!.GetLeftPart(UriPartial.Path) + message.RequestUri.Query);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }));
        return new MainWindowViewModel(new FakeWorkspaceService { WorkspaceToReturn = api.Workspace },
            new RequestExecutionService(factory, oauthTokenService: oauth), oauth);
    }

    [Fact]
    public async Task RunnerDefaultsToTheCollectionsActiveEnvironmentAndTheActiveGlobalEnvironment()
    {
        var api = TwoApis();
        api.Orders.ActiveEnvironmentId = api.Env(api.Orders, "qa").Id;
        var sent = new List<string>();
        var runner = CreateMain(api, sent).CreateCollectionRunner(api.Orders);

        Assert.Same(api.Env(api.Orders, "qa"), runner.SelectedCollectionEnvironment.Environment);
        Assert.Same(api.Global, runner.SelectedEnvironment);
        Assert.Equal(new string?[] { null, "dev", "qa" }, runner.CollectionEnvironments.Select(choice => choice.Environment?.Name));
        await runner.StartCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "https://orders-qa.example.test/ping?idp=idp-qa" }, sent);
    }

    [Fact]
    public async Task RunnerOverridesApplyToThatRunOnlyAndLeaveTheActiveSelectionsUnchanged()
    {
        var api = TwoApis();
        var sent = new List<string>();
        var main = CreateMain(api, sent);
        var runner = main.CreateCollectionRunner(api.Orders);
        var dev = api.Env(api.Orders, "dev");

        runner.SelectedCollectionEnvironment = runner.CollectionEnvironments.Single(choice => choice.Name == "qa");
        runner.SelectedEnvironment = api.OtherGlobal;
        await runner.StartCommand.ExecuteAsync(null);
        runner.SelectedCollectionEnvironment = runner.CollectionEnvironments[0];
        await runner.StartCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "https://orders-qa.example.test/ping?idp=idp-dev", "https://shared-dev.example.test/ping?idp=idp-dev" }, sent);
        Assert.Equal(dev.Id, api.Orders.ActiveEnvironmentId);
        Assert.Same(api.Global, main.SelectedEnvironment);
        Assert.Equal(api.Global.Id, api.Workspace.SelectedEnvironmentId);
        Assert.Equal(dev.Id, main.CreateCollectionRunner(api.Orders).SelectedCollectionEnvironment.Environment?.Id);
    }

    [Fact]
    public async Task FolderRunsFollowTheSameEnvironmentRules()
    {
        var api = TwoApis();
        var folder = new CollectionFolder { Name = "health" };
        api.Orders.Folders.Add(folder);
        api.Orders.Requests[0].FolderId = folder.Id;
        var sent = new List<string>();
        var main = CreateMain(api, sent);

        var runner = main.CreateFolderRunner(folder);
        Assert.Same(api.Env(api.Orders, "dev"), runner.SelectedCollectionEnvironment.Environment);
        await runner.StartCommand.ExecuteAsync(null);
        runner.SelectedCollectionEnvironment = runner.CollectionEnvironments.Single(choice => choice.Name == "qa");
        await runner.StartCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "https://orders-dev.example.test/ping?idp=idp-qa", "https://orders-qa.example.test/ping?idp=idp-qa" }, sent);
        Assert.Equal(api.Env(api.Orders, "dev").Id, api.Orders.ActiveEnvironmentId);
    }

    [Fact]
    public async Task RunnerServiceUsesTheActiveCollectionEnvironmentUnlessTheRunChoosesOne()
    {
        var api = TwoApis();
        var sent = new List<string>();
        ICollectionRunner runner = new CollectionRunner(RequestRunnerTests.Runner(message =>
        {
            sent.Add(message.RequestUri!.GetLeftPart(UriPartial.Authority));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }), workspace: api.Workspace);

        await runner.RunAsync(api.Orders, new() { Environment = api.Global }, null, default);
        await runner.RunAsync(api.Orders, new() { Environment = api.Global, CollectionEnvironment = api.Env(api.Orders, "qa") }, null, default);
        await runner.RunAsync(api.Orders, new() { Environment = api.Global, CollectionEnvironment = null }, null, default);

        Assert.Equal(new[] { "https://orders-dev.example.test", "https://orders-qa.example.test", "https://shared-qa.example.test" }, sent);
    }

    [Fact]
    public async Task MovedRequestResolvesAgainstItsNewCollectionsEnvironments()
    {
        var api = TwoApis();
        api.Billing.ActiveEnvironmentId = api.Env(api.Billing, "qa").Id;
        var request = api.Orders.Requests[0];
        var sent = new List<string>();
        var main = CreateMain(api, sent);
        main.OpenRequestCommand.Execute(request);
        var tab = Assert.Single(main.OpenRequests);

        await tab.SendCommand.ExecuteAsync(null);
        main.CollectionsPanel.MoveRequest(request, api.Billing, null, 0);
        await tab.SendCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "https://orders-dev.example.test/ping?idp=idp-qa", "https://billing-qa.example.test/ping?idp=idp-qa" }, sent);
        Assert.Contains(request, api.Billing.Requests);
        Assert.DoesNotContain(request, api.Orders.Requests);
    }

    private static RequestAuth ClientCredentials() => new()
    {
        Type = AuthType.OAuth2, GrantType = OAuthGrantType.ClientCredentials, ClientId = "client",
        ClientSecret = "client-secret", TokenUrl = "https://auth.test/token"
    };

    private sealed class TokenEndpoint
    {
        public int Exchanges { get; private set; }
        public List<string> Authorizations { get; } = new();
        public HttpResponseMessage? Respond(HttpRequestMessage message)
        {
            if (message.RequestUri!.AbsolutePath != "/token")
            {
                Authorizations.Add(message.Headers.Authorization?.ToString() ?? "");
                return null;
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"access_token\":\"token-" + ++Exchanges + "\",\"expires_in\":3600}") };
        }
        public TestHttpClientFactory Factory() => new(new FakeHttpMessageHandler(message =>
            Task.FromResult(Respond(message) ?? new HttpResponseMessage(HttpStatusCode.OK))));
    }

    [Fact]
    public async Task TokensAreNeverSharedBetweenDifferentEnvironmentPairs()
    {
        var endpoint = new TokenEndpoint();
        var factory = endpoint.Factory();
        var runner = new RequestRunner(new RequestExecutionService(factory, oauthTokenService: new OAuthTokenService(factory, new MemoryTokenCache())));
        var request = new RequestItem { Url = "https://api.test/data", Auth = ClientCredentials() };
        Guid global = Guid.NewGuid(), otherGlobal = Guid.NewGuid(), ordersDev = Guid.NewGuid(), ordersQa = Guid.NewGuid();

        foreach (var (globalId, collectionId) in new (Guid?, Guid?)[] {
            (global, null), (global, ordersDev), (global, ordersQa), (otherGlobal, ordersDev), (global, ordersDev), (global, null) })
        {
            var run = await runner.RunAsync(request, new() { EnvironmentId = globalId, CollectionEnvironmentId = collectionId });
            Assert.True(run.Execution.IsSuccess, run.Execution.ErrorMessage);
        }

        Assert.Equal(4, endpoint.Exchanges);
        Assert.Equal(new[] { "Bearer token-1", "Bearer token-2", "Bearer token-3", "Bearer token-4", "Bearer token-2", "Bearer token-1" },
            endpoint.Authorizations);
    }

    [Fact]
    public async Task TokensCachedBeforeCollectionEnvironmentsMatchOnlyWhenNoCollectionEnvironmentIsActive()
    {
        var endpoint = new TokenEndpoint();
        var factory = endpoint.Factory();
        var cache = new MemoryTokenCache();
        var auth = ClientCredentials();
        var global = Guid.NewGuid();
        var bound = auth.Clone();
        bound.OAuthEnvironmentId = global;
        var stored = JsonSerializer.SerializeToNode(new OAuthToken("legacy", null, DateTimeOffset.UtcNow.AddHours(1)) { Context = OAuthContext.From(bound) })!;
        stored["Context"]!.AsObject().Remove(nameof(OAuthContext.CollectionEnvironmentId));
        await cache.SetAsync(auth.OAuthCacheKey, stored.Deserialize<OAuthToken>()!);
        var runner = new RequestRunner(new RequestExecutionService(factory, oauthTokenService: new OAuthTokenService(factory, cache)));
        var request = new RequestItem { Url = "https://api.test/data", Auth = auth };

        await runner.RunAsync(request, new() { EnvironmentId = global });
        await runner.RunAsync(request, new() { EnvironmentId = global, CollectionEnvironmentId = Guid.NewGuid() });

        Assert.Equal(new[] { "Bearer legacy", "Bearer token-1" }, endpoint.Authorizations);
    }

    [Fact]
    public async Task TokenActionsInTheTabAndCollectionSettingsAddressTheTokenSendUses()
    {
        var api = TwoApis();
        api.Orders.Auth = ClientCredentials();
        api.Orders.Requests[0].Auth = new RequestAuth { Type = AuthType.Inherit };
        var endpoint = new TokenEndpoint();
        var cache = new MemoryTokenCache();
        var oauth = new OAuthTokenService(endpoint.Factory(), cache);
        var main = CreateMain(api, new List<string>(), oauth, endpoint.Respond);
        main.OpenRequestCommand.Execute(api.Orders.Requests[0]);
        var tab = Assert.Single(main.OpenRequests);
        var settings = main.CreateCollectionSettings(api.Orders);

        await settings.AuthEditor.AuthorizeCommand.ExecuteAsync(null);
        await tab.SendCommand.ExecuteAsync(null);
        api.Orders.ActiveEnvironmentId = api.Env(api.Orders, "qa").Id;
        await tab.AuthEditor.AuthorizeCommand.ExecuteAsync(null);
        await tab.SendCommand.ExecuteAsync(null);
        await tab.AuthEditor.GetNewTokenCommand.ExecuteAsync(null);
        await tab.SendCommand.ExecuteAsync(null);
        api.Orders.ActiveEnvironmentId = api.Env(api.Orders, "dev").Id;
        await tab.SendCommand.ExecuteAsync(null);
        await settings.AuthEditor.ClearTokenCommand.ExecuteAsync(null);
        await tab.SendCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "Bearer token-1", "Bearer token-2", "Bearer token-3", "Bearer token-1", "Bearer token-4" }, endpoint.Authorizations);
        Assert.Equal(4, endpoint.Exchanges);
    }

    private sealed class MemoryTokenCache : IOAuthTokenCache
    {
        private readonly List<(string Key, OAuthToken Token)> _entries = new();
        public Task<OAuthToken?> GetAsync(string cacheKey, CancellationToken cancellationToken = default, OAuthContext? context = null) =>
            Task.FromResult<OAuthToken?>(_entries.LastOrDefault(entry => entry.Key == cacheKey && (context is null || entry.Token.Context == context)).Token);
        public Task SetAsync(string cacheKey, OAuthToken token, CancellationToken cancellationToken = default)
        {
            _entries.RemoveAll(entry => entry.Key == cacheKey && entry.Token.Context == token.Context);
            _entries.Add((cacheKey, token));
            return Task.CompletedTask;
        }
        public Task RemoveAsync(string cacheKey, CancellationToken cancellationToken = default)
        {
            _entries.RemoveAll(entry => entry.Key == cacheKey);
            return Task.CompletedTask;
        }
        public Task RemoveAsync(string cacheKey, OAuthContext context, CancellationToken cancellationToken = default)
        {
            _entries.RemoveAll(entry => entry.Key == cacheKey && entry.Token.Context == context);
            return Task.CompletedTask;
        }
    }
}
