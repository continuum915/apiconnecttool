using System.Net;
using System.Net.Sockets;
using System.Text;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class RedirectSafetyTests
{
    [Theory]
    [InlineData(AuthType.Bearer, false)]
    [InlineData(AuthType.Bearer, true)]
    [InlineData(AuthType.Basic, false)]
    [InlineData(AuthType.Basic, true)]
    [InlineData(AuthType.OAuth2, false)]
    [InlineData(AuthType.OAuth2, true)]
    [InlineData(AuthType.ApiKey, false)]
    [InlineData(AuthType.ApiKey, true)]
    public async Task AuthIsRetainedWithinOriginAndNeverRestoredAfterLeaving(AuthType type, bool download)
    {
        var count = 0;
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        {
            count++;
            var header = type == AuthType.ApiKey ? "X-Key" : "Authorization";
            Assert.Equal(count <= 2, message.Headers.Contains(header));
            var response = new HttpResponseMessage(count == 4 ? HttpStatusCode.OK : HttpStatusCode.TemporaryRedirect);
            if (count < 4) response.Headers.Location = new Uri(count switch {
                1 => "/same", 2 => "https://other.test/away", _ => "https://example.test/back" }, UriKind.RelativeOrAbsolute);
            return Task.FromResult(response);
        })), oauthTokenService: new OAuthStub());
        await Run(service, new() { Url = "https://example.test/start", Auth = new() { Type = type,
            Token = new("credential"), Username = "user", Password = new("password"), ApiKeyHeaderName = "X-Key" } }, download);
        Assert.Equal(4, count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealTransportUsesDestinationCookiesAndPreservesLocationQuery(bool download)
    {
        using var source = new TcpListener(IPAddress.Loopback, 0);
        using var destination = new TcpListener(IPAddress.Loopback, 0);
        source.Start(); destination.Start();
        var sourceUrl = $"http://127.0.0.1:{((IPEndPoint)source.LocalEndpoint).Port}";
        var destinationUrl = $"http://127.0.0.1:{((IPEndPoint)destination.LocalEndpoint).Port}/destination?k=1&k=2&sig=a%2Fb%3D+z";
        var store = Path.Combine(Path.GetTempPath(), "ApiConnectToolRedirectTests_" + Guid.NewGuid().ToString("N"));
        var jar = new CookieJarService(store);
        jar.Container.Add(new Uri(sourceUrl), new Cookie("source", "source-value", "/start"));
        jar.Container.Add(new Uri(destinationUrl), new Cookie("destination", "destination-value", "/destination"));
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = jar.Container, UseProxy = false };
        var service = new RequestExecutionService(new TestHttpClientFactory(handler));
        var first = Serve(source, $"HTTP/1.1 302 Found\r\nLocation: {destinationUrl}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        var second = Serve(destination, "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
        await Run(service, new() { Url = sourceUrl + "/start", Auth = new() { Type = AuthType.Bearer, Token = new("credential") },
            QueryParams = new() { new() { Key = "original", Value = "yes" } },
            Headers = new() { new() { Key = "Cookie", Value = "explicit=private" } } }, download);
        var initial = await first;
        var redirected = await second;
        Assert.Contains("Authorization: Bearer credential", initial);
        Assert.Contains("explicit=private", initial);
        Assert.Contains("source=source-value", initial);
        Assert.Contains("GET /destination?k=1&k=2&sig=a%2Fb%3D+z HTTP/1.1", redirected);
        Assert.Contains("destination=destination-value", redirected);
        Assert.DoesNotContain("Authorization:", redirected);
        Assert.DoesNotContain("explicit=", redirected);
        Assert.DoesNotContain("source=", redirected);
        Assert.DoesNotContain("original=", redirected);
    }

    private static async Task<string> Serve(TcpListener listener, string response)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = await listener.AcceptTcpClientAsync(timeout.Token);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var lines = new List<string>();
        while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 } line) lines.Add(line);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(response), timeout.Token);
        return string.Join("\n", lines);
    }

    private sealed class OAuthStub : IOAuthTokenService
    {
        public Task<string> GetValidAccessTokenAsync(RequestAuth auth, CancellationToken cancellationToken = default) => Task.FromResult("oauth-credential");
        public Task<OAuthToken> RequestClientCredentialsTokenAsync(RequestAuth auth, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OAuthToken> RefreshTokenAsync(RequestAuth auth, string refreshToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OAuthToken> AuthorizeWithPkceAsync(RequestAuth auth, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ClearCachedTokenAsync(RequestAuth auth, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(false, "https://example.test:443/next", true)]
    [InlineData(true, "/next", true)]
    [InlineData(false, "https://other.test/next", false)]
    [InlineData(true, "https://example.test:444/next", false)]
    [InlineData(false, "http://example.test/next", false)]
    [InlineData(true, "https://example.test/next", false)]
    public async Task CredentialsFollowOnlySameOrigin(bool download, string location, bool retained)
    {
        var count = 0;
        var start = location.StartsWith("http:") ? "http://example.test:81/start"
            : download && location == "https://example.test/next" ? "http://example.test/start" : "https://example.test/start";
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        {
            count++;
            if (count > 1)
            {
                Assert.Equal(retained, message.Headers.Contains("Authorization"));
                Assert.Equal(retained, message.Headers.Contains("Cookie"));
                Assert.Equal(retained, message.Headers.Contains("X-Key"));
                Assert.Equal(retained, message.Headers.Contains("X-Secret"));
                Assert.True(message.Headers.Contains("X-Public"));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }
            var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
            return Task.FromResult(response);
        })));
        await Run(service, new() { Url = start, Auth = new() { Type = AuthType.ApiKey, ApiKeyHeaderName = "X-Key", Token = new("credential") },
            Headers = new() { new() { Key = "Authorization", Value = "Bearer manual" }, new() { Key = "Cookie", Value = "session=manual" },
                new() { Key = "X-Secret", Value = "prefix {{secret}}" }, new() { Key = "X-Public", Value = "public" } } }, download,
            new() { Variables = new Dictionary<string, string> { ["secret"] = "private-value" }, SecretVariableNames = new HashSet<string> { "secret" } });
        Assert.Equal(2, count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpsDowngradeStopsBeforeSecondSend(bool download)
    {
        var count = 0;
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
        {
            count++;
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("http://example.test/secret");
            return Task.FromResult(response);
        })));
        var request = new RequestItem { Url = "https://example.test/start" };
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
        if (download)
        {
            var result = await service.DownloadAsync(request, path);
            Assert.Equal(RequestExecutionFailureKind.Validation, result.FailureKind);
            Assert.Equal("Redirect blocked because it changes HTTPS to HTTP. Use an HTTPS destination or correct the server's redirect.", result.ErrorMessage);
            Assert.False(File.Exists(path));
            Assert.False(File.Exists(path + ".apiconnecttoolpart"));
        }
        else
        {
            var result = await service.ExecuteAsync(request);
            Assert.Equal(RequestExecutionFailureKind.Validation, result.FailureKind);
            Assert.Equal("Redirect blocked because it changes HTTPS to HTTP. Use an HTTPS destination or correct the server's redirect.", result.ErrorMessage);
        }
        Assert.Equal(1, count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocationQueriesArePreservedAcrossHops(bool download)
    {
        var seen = new List<string>();
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        {
            seen.Add(message.RequestUri!.AbsoluteUri);
            var response = new HttpResponseMessage(seen.Count < 3 ? HttpStatusCode.Found : HttpStatusCode.OK);
            if (seen.Count < 3) response.Headers.Location = new Uri(seen.Count == 1
                ? "/next?k=1&k=2&sig=a%2Fb%3D+z" : "https://other.test/final?x=%252F&x=2", UriKind.RelativeOrAbsolute);
            return Task.FromResult(response);
        })));
        var request = new RequestItem { Url = "https://example.test/start", Auth = new() { Type = AuthType.ApiKey,
            ApiKeyQueryParam = "key", Token = new("credential") }, QueryParams = new() {
                new() { Key = "row", Value = "yes" }, new() { Key = "disabled", Value = "no", IsEnabled = false } } };
        await Run(service, request, download);
        Assert.Equal(new[] { "https://example.test/start?row=yes&key=credential",
            "https://example.test/next?k=1&k=2&sig=a%2Fb%3D+z", "https://other.test/final?x=%252F&x=2" }, seen);
    }

    private static async Task Run(RequestExecutionService service, RequestItem request, bool download,
        RequestExecutionOptions? options = null)
    {
        if (!download) Assert.True((await service.ExecuteAsync(request, options)).IsSuccess);
        else
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
            try { Assert.True((await service.DownloadAsync(request, path, options)).IsSuccess); }
            finally { File.Delete(path); File.Delete(path + ".apiconnecttoolpart"); }
        }
    }
}
