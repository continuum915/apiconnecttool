using System.Net;
using System.Net.Http;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Services.Localization;
using ApiConnectTool.Tests.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class TokenActionsTests
{
    private static readonly Guid Dev = Guid.NewGuid();
    private static readonly Guid Prod = Guid.NewGuid();

    [Fact]
    public async Task ClearToken_RemovesOnlyTheActiveEnvironmentsToken()
    {
        var server = new TokenServer();
        var cache = new ContextCache();
        var auth = ClientCredentials();
        Guid? environment = Dev;
        var editor = new AuthEditorViewModel(auth, server.Service(cache)) { EnvironmentIdsProvider = () => new(environment, null) };
        await editor.AuthorizeCommand.ExecuteAsync(null);
        environment = Prod;
        await editor.AuthorizeCommand.ExecuteAsync(null);

        environment = Dev;
        await editor.ClearTokenCommand.ExecuteAsync(null);

        Assert.Equal(DefaultStrings.Get("Str.Auth.TokenCleared"), editor.AuthorizationMessage);
        Assert.Null(await cache.GetAsync(auth.OAuthCacheKey, context: Bound(auth, Dev)));
        Assert.Equal("token-2", (await cache.GetAsync(auth.OAuthCacheKey, context: Bound(auth, Prod)))!.AccessToken);
    }

    [Fact]
    public async Task GetNewToken_ClientCredentials_ReplacesStillValidToken()
    {
        var server = new TokenServer();
        var cache = new ContextCache();
        var auth = ClientCredentials();
        var editor = new AuthEditorViewModel(auth, server.Service(cache)) { EnvironmentIdsProvider = () => new(Dev, null) };
        await editor.AuthorizeCommand.ExecuteAsync(null);

        await editor.GetNewTokenCommand.ExecuteAsync(null);

        Assert.Equal(DefaultStrings.Get("Str.Auth.NewTokenObtained"), editor.AuthorizationMessage);
        Assert.Equal(2, server.Calls);
        Assert.Equal("token-2", (await cache.GetAsync(auth.OAuthCacheKey, context: Bound(auth, Dev)))!.AccessToken);
    }

    [Fact]
    public async Task InheritedAuth_ClearAndGetNewTokenUseTheParentsResolvedContext()
    {
        var server = new TokenServer();
        var cache = new ContextCache();
        var parent = ClientCredentials();
        parent.ClientId = "{{clientId}}";
        var collection = new RequestCollection { Name = "API", Auth = parent };
        collection.Variables.Add(new EnvironmentVariable { Key = "clientId", InitialValue = new EncryptedString("resolved-client") });
        var resolved = parent.Clone(preserveCacheKey: true);
        resolved.ClientId = "resolved-client";
        var editor = new AuthEditorViewModel(new RequestAuth { Type = AuthType.Inherit }, server.Service(cache),
            () => new EffectiveAuth(collection.Auth, collection.Name));
        editor.ConfigureScopes(null, collection, () => null);

        await editor.GetNewTokenCommand.ExecuteAsync(null);

        Assert.Equal(1, server.Calls);
        Assert.Contains("client_id=resolved-client", server.LastBody);
        Assert.Equal("token-1", (await cache.GetAsync(parent.OAuthCacheKey, context: OAuthContext.From(resolved)))!.AccessToken);

        await editor.ClearTokenCommand.ExecuteAsync(null);

        Assert.Equal(DefaultStrings.Get("Str.Auth.TokenCleared"), editor.AuthorizationMessage);
        Assert.Null(await cache.GetAsync(parent.OAuthCacheKey, context: OAuthContext.From(resolved)));
    }

    [Fact]
    public async Task AuthorizationCode_ClearTokenRemovesItAndGetNewTokenDoesNotCallTheNetwork()
    {
        var server = new TokenServer();
        var cache = new ContextCache();
        var auth = new RequestAuth
        {
            Type = AuthType.OAuth2, GrantType = OAuthGrantType.AuthorizationCodePkce, ClientId = "client",
            TokenUrl = "https://auth.test/token", AuthorizationUrl = "https://auth.test/authorize"
        };
        await cache.SetAsync(auth.OAuthCacheKey, new OAuthToken("interactive", "refresh", DateTimeOffset.UtcNow.AddHours(1))
            { Context = Bound(auth, Dev) });
        var editor = new AuthEditorViewModel(auth, server.Service(cache)) { EnvironmentIdsProvider = () => new(Dev, null) };

        await editor.GetNewTokenCommand.ExecuteAsync(null);
        Assert.Equal(DefaultStrings.Get("Str.Auth.NewTokenClientCredentialsOnly"), editor.AuthorizationMessage);
        Assert.NotNull(await cache.GetAsync(auth.OAuthCacheKey, context: Bound(auth, Dev)));

        await editor.ClearTokenCommand.ExecuteAsync(null);

        Assert.Null(await cache.GetAsync(auth.OAuthCacheKey, context: Bound(auth, Dev)));
        Assert.Equal(0, server.Calls);
    }

    [Fact]
    public async Task GetNewToken_FailureMessageIsRedacted()
    {
        var server = new TokenServer
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{"error":"invalid_client","error_description":"bad secret private-client-secret"}""")
            }
        };
        var auth = ClientCredentials();
        auth.ClientSecret = "{{secret}}";
        var collection = new RequestCollection();
        collection.Variables.Add(new EnvironmentVariable { Key = "secret", InitialValue = new EncryptedString("private-client-secret"), IsSecret = true });
        var editor = new AuthEditorViewModel(auth, server.Service(new ContextCache()));
        editor.ConfigureScopes(null, collection, () => null);

        await editor.GetNewTokenCommand.ExecuteAsync(null);

        Assert.Contains("invalid_client", editor.AuthorizationMessage);
        Assert.DoesNotContain("private-client-secret", editor.AuthorizationMessage);
    }

    private static RequestAuth ClientCredentials() => new()
    {
        Type = AuthType.OAuth2, GrantType = OAuthGrantType.ClientCredentials, ClientId = "client",
        ClientSecret = "client-secret", TokenUrl = "https://auth.test/token"
    };

    private static OAuthContext Bound(RequestAuth auth, Guid environment)
    {
        var bound = auth.Clone(preserveCacheKey: true);
        bound.OAuthEnvironmentId = environment;
        return OAuthContext.From(bound);
    }

    private sealed class TokenServer
    {
        public int Calls { get; private set; }
        public string LastBody { get; private set; } = string.Empty;
        public Func<int, HttpResponseMessage>? Respond { get; init; }

        public OAuthTokenService Service(IOAuthTokenCache cache) =>
            new(new TestHttpClientFactory(new FakeHttpMessageHandler(async request =>
            {
                LastBody = await request.Content!.ReadAsStringAsync();
                Calls++;
                return Respond?.Invoke(Calls) ?? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"token-" + Calls + "\",\"expires_in\":3600}")
                };
            })), cache);
    }

    private sealed class ContextCache : IOAuthTokenCache
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
