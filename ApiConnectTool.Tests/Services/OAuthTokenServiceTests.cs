using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class OAuthTokenServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnresolvedClientIdStopsExchangeAndBrowserBeforeAnySideEffect(bool interactive)
    {
        var calls = 0;
        var service = new OAuthTokenService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
        {
            calls++;
            return Task.FromResult(JsonResponse("{\"access_token\":\"unexpected\"}"));
        })), new InMemoryTokenCache());
        var auth = new RequestAuth { Type = AuthType.OAuth2, ClientId = "{{missing}}",
            TokenUrl = "https://auth.test/token", AuthorizationUrl = "https://auth.test/authorize",
            RedirectUri = "invalid-listener-uri", GrantType = interactive ? OAuthGrantType.AuthorizationCodePkce : OAuthGrantType.ClientCredentials };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (interactive) await service.AuthorizeWithPkceAsync(auth);
            else await service.GetValidAccessTokenAsync(auth);
        });
        Assert.Contains("Client ID", error.Message);
        Assert.Contains("Define or enable", error.Message);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void GenerateCodeVerifier_ProducesUrlSafeStringOfSufficientLength()
    {
        var verifier = OAuthTokenService.GenerateCodeVerifier();

        Assert.True(verifier.Length >= 43);
        Assert.DoesNotContain('+', verifier);
        Assert.DoesNotContain('/', verifier);
        Assert.DoesNotContain('=', verifier);
    }

    [Fact]
    public void GenerateCodeVerifier_ProducesDifferentValuesEachCall()
    {
        Assert.NotEqual(OAuthTokenService.GenerateCodeVerifier(), OAuthTokenService.GenerateCodeVerifier());
    }

    [Fact]
    public void GenerateCodeChallenge_IsDeterministicSha256OfVerifier()
    {
        var challengeA = OAuthTokenService.GenerateCodeChallenge("fixed-verifier-value");
        var challengeB = OAuthTokenService.GenerateCodeChallenge("fixed-verifier-value");

        Assert.Equal(challengeA, challengeB);
        Assert.NotEqual("fixed-verifier-value", challengeA);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_ClientCredentials_NoCache_RequestsAndCachesFreshToken()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpMessageHandler(async request =>
        {
            captured = request;
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("grant_type=client_credentials", body);
            Assert.Contains("client_id=my-client", body);
            return JsonResponse("{\"access_token\":\"token-1\",\"expires_in\":3600}");
        });
        var cache = new InMemoryTokenCache();
        var service = new OAuthTokenService(new TestHttpClientFactory(handler), cache);
        var auth = new RequestAuth
        {
            Type = AuthType.OAuth2,
            GrantType = OAuthGrantType.ClientCredentials,
            ClientId = "my-client",
            TokenUrl = "https://auth.example.test/token"
        };

        var token = await service.GetValidAccessTokenAsync(auth);

        Assert.Equal("token-1", token);
        Assert.NotNull(captured);
        var cached = await cache.GetAsync(auth.OAuthCacheKey);
        Assert.Equal("token-1", cached!.AccessToken);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_UnexpiredCachedToken_ReturnsCachedTokenWithoutNetworkCall()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Must not call the network for a still-valid token."));
        var cache = new InMemoryTokenCache();
        var auth = new RequestAuth { Type = AuthType.OAuth2, GrantType = OAuthGrantType.ClientCredentials, ClientId = "c", TokenUrl = "https://auth.example.test/token" };
        await cache.SetAsync(auth.OAuthCacheKey, new OAuthToken("cached-token", null, DateTimeOffset.UtcNow.AddHours(1)) { Context = OAuthContext.From(auth) });
        var service = new OAuthTokenService(new TestHttpClientFactory(handler), cache);

        var token = await service.GetValidAccessTokenAsync(auth);

        Assert.Equal("cached-token", token);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_ExpiredWithRefreshToken_RefreshesTransparently()
    {
        var handler = new FakeHttpMessageHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("grant_type=refresh_token", body);
            Assert.Contains("refresh_token=old-refresh", body);
            return JsonResponse("{\"access_token\":\"refreshed-token\",\"refresh_token\":\"new-refresh\",\"expires_in\":3600}");
        });
        var cache = new InMemoryTokenCache();
        var auth = new RequestAuth { Type = AuthType.OAuth2, GrantType = OAuthGrantType.ClientCredentials, ClientId = "c", TokenUrl = "https://auth.example.test/token" };
        await cache.SetAsync(auth.OAuthCacheKey, new OAuthToken("expired-token", "old-refresh", DateTimeOffset.UtcNow.AddSeconds(-10)) { Context = OAuthContext.From(auth) });
        var service = new OAuthTokenService(new TestHttpClientFactory(handler), cache);

        var token = await service.GetValidAccessTokenAsync(auth);

        Assert.Equal("refreshed-token", token);
        var cached = await cache.GetAsync(auth.OAuthCacheKey);
        Assert.Equal("new-refresh", cached!.RefreshToken);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_ExpiredNoRefreshTokenAndPkceGrant_ThrowsReauthenticateError()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Must not call the network without user interaction."));
        var cache = new InMemoryTokenCache();
        var auth = new RequestAuth { Type = AuthType.OAuth2, GrantType = OAuthGrantType.AuthorizationCodePkce, ClientId = "c", TokenUrl = "https://auth.example.test/token" };
        await cache.SetAsync(auth.OAuthCacheKey, new OAuthToken("expired-token", null, DateTimeOffset.UtcNow.AddSeconds(-10)));
        var service = new OAuthTokenService(new TestHttpClientFactory(handler), cache);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetValidAccessTokenAsync(auth));

        Assert.Contains("re-authenticate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_NoCacheAndPkceGrant_ThrowsReauthenticateErrorWithoutNetworkCall()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Must not call the network without user interaction."));
        var cache = new InMemoryTokenCache();
        var auth = new RequestAuth { Type = AuthType.OAuth2, GrantType = OAuthGrantType.AuthorizationCodePkce, ClientId = "c", TokenUrl = "https://auth.example.test/token" };
        var service = new OAuthTokenService(new TestHttpClientFactory(handler), cache);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetValidAccessTokenAsync(auth));
    }

    [Fact]
    public async Task RequestClientCredentialsTokenAsync_MissingClientId_ThrowsValidationError()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Must not call the network."));
        var service = new OAuthTokenService(new TestHttpClientFactory(handler), new InMemoryTokenCache());
        var auth = new RequestAuth { Type = AuthType.OAuth2, GrantType = OAuthGrantType.ClientCredentials, TokenUrl = "https://auth.example.test/token" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestClientCredentialsTokenAsync(auth));
    }

    [Fact]
    public async Task RequestClientCredentialsTokenAsync_ErrorResponse_ReportsStatusAndProviderError()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(JsonResponse(
            "{\"error\":\"invalid_client\",\"error_description\":\"Client authentication failed.\"}", HttpStatusCode.Unauthorized)));
        var service = new OAuthTokenService(new TestHttpClientFactory(handler), new InMemoryTokenCache());

        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.RequestClientCredentialsTokenAsync(ClientCredentialsAuth()));

        Assert.Contains("401", ex.Message);
        Assert.Contains("invalid_client: Client authentication failed.", ex.Message);
    }

    [Fact]
    public async Task ErrorResponse_NonJsonBodyIsNotShown()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("<html>internal stack trace</html>", Encoding.UTF8, "text/html")
        }));
        var service = new OAuthTokenService(new TestHttpClientFactory(handler), new InMemoryTokenCache());

        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.RequestClientCredentialsTokenAsync(ClientCredentialsAuth()));

        Assert.Contains("400", ex.Message);
        Assert.DoesNotContain("stack trace", ex.Message);
    }

    [Fact]
    public async Task ErrorResponse_RedactsFormSecretsAndLimitsLength()
    {
        var description = "secret super-secret-value and refresh old-refresh-value rejected " + new string('x', 1000);
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(JsonResponse(
            "{\"error\":\"invalid_grant\",\"error_description\":\"" + description + "\"}", HttpStatusCode.BadRequest)));
        var service = new OAuthTokenService(new TestHttpClientFactory(handler), new InMemoryTokenCache());
        var auth = ClientCredentialsAuth();
        auth.ClientSecret = "super-secret-value";

        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.RefreshTokenAsync(auth, "old-refresh-value"));

        Assert.Contains("invalid_grant", ex.Message);
        Assert.DoesNotContain("super-secret-value", ex.Message);
        Assert.DoesNotContain("old-refresh-value", ex.Message);
        Assert.DoesNotContain(new string('x', 301), ex.Message);
        Assert.True(ex.Message.Length < 500, ex.Message);
    }

    [Theory]
    [InlineData("7200", 7200)]
    [InlineData("\"7200\"", 7200)]
    [InlineData("\" 120 \"", 120)]
    [InlineData("\"soon\"", 3600)]
    [InlineData("\"1.5\"", 3600)]
    [InlineData("true", 3600)]
    [InlineData("null", 3600)]
    [InlineData("-5", 3600)]
    [InlineData("12.5", 3600)]
    public async Task ExpiresIn_AcceptsNumberOrIntegerTextAndFallsBackOtherwise(string expiresJson, int expectedSeconds)
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(JsonResponse(
            "{\"access_token\":\"token\",\"expires_in\":" + expiresJson + "}")));
        var service = new OAuthTokenService(new TestHttpClientFactory(handler), new InMemoryTokenCache());
        var before = DateTimeOffset.UtcNow;

        var token = await service.RequestClientCredentialsTokenAsync(ClientCredentialsAuth());

        var lifetime = token.ExpiresAtUtc - before;
        Assert.InRange(lifetime.TotalSeconds, expectedSeconds - 5, expectedSeconds + 5);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[\"access_token\"]")]
    [InlineData("\"just a string\"")]
    [InlineData("{\"access_token\":12345}")]
    [InlineData("{\"access_token\":{\"value\":\"x\"}}")]
    [InlineData("{\"token_type\":\"bearer\"}")]
    public async Task MalformedTokenResponse_ReportsLocalizedInvalidResponse(string body)
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(JsonResponse(body)));
        var service = new OAuthTokenService(new TestHttpClientFactory(handler), new InMemoryTokenCache());

        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.RequestClientCredentialsTokenAsync(ClientCredentialsAuth()));

        Assert.Equal(ApiConnectTool.Services.Localization.DefaultStrings.Get("Str.OAuth.InvalidResponse"), ex.Message);
    }

    [Fact]
    public async Task NonStringRefreshToken_IsIgnoredAndFallbackApplies()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(JsonResponse(
            "{\"access_token\":\"new-token\",\"refresh_token\":42,\"expires_in\":60}")));
        var service = new OAuthTokenService(new TestHttpClientFactory(handler), new InMemoryTokenCache());

        var token = await service.RefreshTokenAsync(ClientCredentialsAuth(), "kept-refresh");

        Assert.Equal("new-token", token.AccessToken);
        Assert.Equal("kept-refresh", token.RefreshToken);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task RejectedRefresh_ClientCredentials_DropsEntryAndExchangesOnce(HttpStatusCode rejection)
    {
        var grants = new List<string>();
        var handler = new FakeHttpMessageHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            grants.Add(body.Contains("grant_type=refresh_token") ? "refresh" : "client_credentials");
            return body.Contains("grant_type=refresh_token")
                ? JsonResponse("{\"error\":\"invalid_grant\"}", rejection)
                : JsonResponse("{\"access_token\":\"fresh-token\",\"expires_in\":3600}");
        });
        var cache = new InMemoryTokenCache();
        var auth = ClientCredentialsAuth();
        await cache.SetAsync(auth.OAuthCacheKey, Expired("old-refresh", auth));
        var service = new OAuthTokenService(new TestHttpClientFactory(handler), cache);

        Assert.Equal("fresh-token", await service.GetValidAccessTokenAsync(auth));

        Assert.Equal(["refresh", "client_credentials"], grants);
        var cached = await cache.GetAsync(auth.OAuthCacheKey, context: OAuthContext.From(auth));
        Assert.Equal("fresh-token", cached!.AccessToken);
        Assert.Null(cached.RefreshToken);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task RejectedRefresh_AuthorizationCode_DropsOnlyThatContextAndAsksToAuthorize(HttpStatusCode rejection)
    {
        var calls = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            calls++;
            return Task.FromResult(JsonResponse("{\"error\":\"invalid_grant\"}", rejection));
        });
        var cache = new InMemoryTokenCache();
        var auth = PkceAuth();
        var otherEnvironment = PkceAuth();
        otherEnvironment.TokenUrl = "https://other-env.example.test/token";
        await cache.SetAsync(auth.OAuthCacheKey, Expired("old-refresh", auth));
        await cache.SetAsync(auth.OAuthCacheKey, Expired("other-refresh", otherEnvironment));
        var service = new OAuthTokenService(new TestHttpClientFactory(handler), cache);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetValidAccessTokenAsync(auth));

        Assert.Contains("Click Authorize", ex.Message);
        Assert.Contains(((int)rejection).ToString(System.Globalization.CultureInfo.InvariantCulture), ex.Message);
        Assert.Equal(1, calls);
        Assert.Null(await cache.GetAsync(auth.OAuthCacheKey, context: OAuthContext.From(auth)));
        Assert.Equal("other-refresh", (await cache.GetAsync(auth.OAuthCacheKey, context: OAuthContext.From(otherEnvironment)))!.RefreshToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetValidAccessTokenAsync(auth));
        Assert.Equal(1, calls);
    }

    public static TheoryData<OAuthGrantType, string> TransientRefreshFailures => new()
    {
        { OAuthGrantType.ClientCredentials, "network" },
        { OAuthGrantType.ClientCredentials, "timeout" },
        { OAuthGrantType.ClientCredentials, "server-error" },
        { OAuthGrantType.AuthorizationCodePkce, "network" },
        { OAuthGrantType.AuthorizationCodePkce, "timeout" },
        { OAuthGrantType.AuthorizationCodePkce, "server-error" },
    };

    [Theory]
    [MemberData(nameof(TransientRefreshFailures))]
    public async Task TransientRefreshFailure_KeepsCachedRefreshToken(OAuthGrantType grant, string failure)
    {
        var calls = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            calls++;
            return failure switch
            {
                "network" => throw new HttpRequestException("connection refused"),
                "timeout" => throw new TaskCanceledException("timed out"),
                _ => Task.FromResult(JsonResponse("{\"error\":\"temporarily_unavailable\"}", HttpStatusCode.ServiceUnavailable))
            };
        });
        var cache = new InMemoryTokenCache();
        var auth = grant == OAuthGrantType.ClientCredentials ? ClientCredentialsAuth() : PkceAuth();
        await cache.SetAsync(auth.OAuthCacheKey, Expired("keep-me", auth));
        var service = new OAuthTokenService(new TestHttpClientFactory(handler), cache);

        var ex = await Record.ExceptionAsync(() => service.GetValidAccessTokenAsync(auth));

        Assert.NotNull(ex);
        if (failure == "network")
        {
            Assert.Equal(ApiConnectTool.Services.Localization.DefaultStrings.Get("Str.OAuth.ConnectionFailed"), ex.Message);
        }
        Assert.Equal(1, calls);
        Assert.Equal("keep-me", (await cache.GetAsync(auth.OAuthCacheKey, context: OAuthContext.From(auth)))!.RefreshToken);
    }

    private static OAuthToken Expired(string refreshToken, RequestAuth auth) =>
        new("expired-access", refreshToken, DateTimeOffset.UtcNow.AddMinutes(-5)) { Context = OAuthContext.From(auth) };

    private static RequestAuth PkceAuth() => new()
    {
        Type = AuthType.OAuth2, GrantType = OAuthGrantType.AuthorizationCodePkce, ClientId = "c",
        TokenUrl = "https://auth.example.test/token", AuthorizationUrl = "https://auth.example.test/authorize"
    };

    private static RequestAuth ClientCredentialsAuth() => new()
    {
        Type = AuthType.OAuth2, GrantType = OAuthGrantType.ClientCredentials, ClientId = "c",
        TokenUrl = "https://auth.example.test/token"
    };

    [Fact]
    public async Task ChangedSecret_UsesIndependentToken_AndReturningReusesOriginal()
    {
        var calls = 0;
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(JsonResponse(
            "{\"access_token\":\"token-" + ++calls + "\",\"expires_in\":3600}")));
        var service = new OAuthTokenService(new TestHttpClientFactory(handler), new InMemoryTokenCache());
        var auth = new RequestAuth { GrantType = OAuthGrantType.ClientCredentials, ClientId = "client",
            TokenUrl = "https://auth.example.test/token", ClientSecret = "secret-a" };
        Assert.Equal("token-1", await service.GetValidAccessTokenAsync(auth));
        auth.ClientSecret = "secret-b";
        Assert.Equal("token-2", await service.GetValidAccessTokenAsync(auth));
        auth.ClientSecret = "secret-a";
        Assert.Equal("token-1", await service.GetValidAccessTokenAsync(auth));
        Assert.Equal(2, calls);
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class InMemoryTokenCache : IOAuthTokenCache
    {
        private readonly Dictionary<(string, OAuthContext?), OAuthToken> _entries = new();

        public Task<OAuthToken?> GetAsync(string cacheKey, CancellationToken cancellationToken = default, OAuthContext? context = null) =>
            Task.FromResult(context is null ? System.Linq.Enumerable.LastOrDefault(System.Linq.Enumerable.Where(_entries, entry => entry.Key.Item1 == cacheKey)).Value : (_entries.TryGetValue((cacheKey, context), out var token) ? token : null));

        public Task SetAsync(string cacheKey, OAuthToken token, CancellationToken cancellationToken = default)
        {
            _entries[(cacheKey, token.Context)] = token;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string cacheKey, OAuthContext context, CancellationToken cancellationToken = default)
        {
            _entries.Remove((cacheKey, context));
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string cacheKey, CancellationToken cancellationToken = default)
        {
            _entries.Remove((cacheKey, null));
            return Task.CompletedTask;
        }
    }
}
