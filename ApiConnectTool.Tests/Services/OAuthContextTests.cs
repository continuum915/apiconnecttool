using System.Net;
using System.Text.Json;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class OAuthContextTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"apiconnecttool-context-{Guid.NewGuid():N}.json");
    private OAuthTokenCache Cache() => new(new WindowsDpapiSecretStore(), _path);
    private static RequestAuth Auth() => new() { Type = AuthType.OAuth2, GrantType = OAuthGrantType.ClientCredentials,
        ClientId = "client-a", ClientSecret = "private-client-secret", TokenUrl = "https://a.test/token",
        AuthorizationUrl = "https://a.test/authorize", Scope = "read", RedirectUri = "http://127.0.0.1/callback" };

    [Theory]
    [InlineData("environment")]
    [InlineData("grant")]
    [InlineData("authorization")]
    [InlineData("token")]
    [InlineData("client")]
    [InlineData("secret")]
    [InlineData("scope")]
    [InlineData("redirect")]
    public async Task ChangedContextNeverUsesOriginalAccessOrRefreshCredentials(string field)
    {
        var auth = Auth();
        var original = auth.Clone();
        var cache = Cache();
        await cache.SetAsync(auth.OAuthCacheKey, new("a-access", "a-refresh", DateTimeOffset.UtcNow.AddMinutes(-1))
            { Context = OAuthContext.From(auth) });
        switch (field)
        {
            case "environment": auth.OAuthEnvironmentId = Guid.NewGuid(); break;
            case "grant": auth.GrantType = OAuthGrantType.AuthorizationCodePkce; break;
            case "authorization": auth.AuthorizationUrl = "https://b.test/authorize"; break;
            case "token": auth.TokenUrl = "https://b.test/token"; break;
            case "client": auth.ClientId = "client-b"; break;
            case "secret": auth.ClientSecret = "other-secret"; break;
            case "scope": auth.Scope = "write"; break;
            case "redirect": auth.RedirectUri = "http://127.0.0.1/other"; break;
        }
        var calls = 0;
        var factory = new TestHttpClientFactory(new FakeHttpMessageHandler(async request => {
            calls++;
            var form = await request.Content!.ReadAsStringAsync();
            Assert.DoesNotContain("a-refresh", form);
            Assert.DoesNotContain("refresh_token", form);
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"access_token":"b-access","expires_in":3600}""") };
        }));
        var service = new OAuthTokenService(factory, cache);
        if (field == "grant")
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetValidAccessTokenAsync(auth));
            Assert.Equal(0, calls);
        }
        else Assert.Equal("b-access", await service.GetValidAccessTokenAsync(auth));
        Assert.Equal("a-refresh", (await Cache().GetAsync(original.OAuthCacheKey, context: OAuthContext.From(original)))!.RefreshToken);
    }

    [Fact]
    public async Task RunnerSeparatesEnvironmentsAndReusesPersistedOriginalContext()
    {
        var exchanges = 0;
        var observed = new List<string>();
        var factory = new TestHttpClientFactory(new FakeHttpMessageHandler(request => {
            if (request.RequestUri!.AbsolutePath == "/token")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                    "{\"access_token\":\"issued-" + ++exchanges + "\",\"expires_in\":3600}") });
            observed.Add(request.Headers.Authorization!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }));
        RequestRunner Runner() => new(new RequestExecutionService(factory, oauthTokenService: new OAuthTokenService(factory, Cache())));
        var auth = Auth();
        auth.Scope = "{{scope}}";
        var request = new RequestItem { Url = "https://api.test/data", Auth = auth };
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        foreach (var environment in new[] { a, b, a })
        {
            var run = await Runner().RunAsync(request, new() { EnvironmentId = environment,
                Variables = new Dictionary<string, string> { ["scope"] = "resolved-scope" } });
            Assert.True(run.Execution.IsSuccess, run.Execution.ErrorMessage);
        }
        Assert.Equal(2, exchanges);
        Assert.Equal(new[] { "Bearer issued-1", "Bearer issued-2", "Bearer issued-1" }, observed);
        var disk = File.ReadAllText(_path);
        Assert.DoesNotContain("issued-", disk);
        Assert.DoesNotContain("private-client-secret", disk);
        Assert.DoesNotContain("resolved-scope", disk);
        Assert.DoesNotContain(a.ToString(), disk);
        using var json = JsonDocument.Parse(disk);
        Assert.Equal(2, json.RootElement.GetProperty("Version").GetInt32());
        Assert.Single(json.RootElement.GetProperty("Entries").EnumerateObject());
        Assert.Equal(auth.OAuthCacheKey, json.RootElement.GetProperty("Entries").EnumerateObject().Single().Name);
    }

    [Fact]
    public async Task IndependentAuthIdentitiesDoNotShareTokensInTheSameContext()
    {
        var first = Auth();
        var second = first.Clone(preserveCacheKey: false);
        var calls = 0;
        var service = new OAuthTokenService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                "{\"access_token\":\"independent-" + ++calls + "\"}") }))), Cache());
        Assert.Equal("independent-1", await service.GetValidAccessTokenAsync(first));
        Assert.Equal("independent-2", await service.GetValidAccessTokenAsync(second));
        Assert.Equal("independent-1", await service.GetValidAccessTokenAsync(first));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task LegacyContextlessTokensRequireFreshExchangeOrAuthorization()
    {
        var auth = Auth();
        File.WriteAllText(_path, JsonSerializer.Serialize(new Dictionary<string, object> {
            [auth.OAuthCacheKey] = new { AccessToken = "legacy-access", RefreshToken = "legacy-refresh", ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1) } }));
        var calls = 0;
        var service = new OAuthTokenService(new TestHttpClientFactory(new FakeHttpMessageHandler(async request => {
            calls++;
            Assert.DoesNotContain("legacy", await request.Content!.ReadAsStringAsync());
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"access_token":"fresh"}""") };
        })), Cache());
        auth.GrantType = OAuthGrantType.AuthorizationCodePkce;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetValidAccessTokenAsync(auth));
        auth.GrantType = OAuthGrantType.ClientCredentials;
        Assert.Equal("fresh", await service.GetValidAccessTokenAsync(auth));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("error")]
    [InlineData("malformed")]
    [InlineData("transport")]
    public async Task TokenErrorsDoNotExposeResponseOrTransportSecrets(string failure)
    {
        var factory = new TestHttpClientFactory(new FakeHttpMessageHandler(_ => {
            if (failure == "transport") throw new HttpRequestException("private-client-secret");
            return Task.FromResult(new HttpResponseMessage(failure == "error" ? HttpStatusCode.BadRequest : HttpStatusCode.OK)
                { Content = new StringContent("private-client-secret") });
        }));
        var service = new OAuthTokenService(factory, Cache());
        var error = await Assert.ThrowsAnyAsync<Exception>(() => service.GetValidAccessTokenAsync(Auth()));
        Assert.DoesNotContain("private-client-secret", error.ToString());
    }

    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }
}
