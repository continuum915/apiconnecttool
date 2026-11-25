using System.Net;
using System.Text;
using System.Text.Json;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class CookieHistoryRedactionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Send_PersistsOnlyRedactedSessionMaterial(bool fail)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ApiConnectToolCookieHistory_" + Guid.NewGuid());
        try
        {
            var jar = new CookieJarService(directory, new WindowsDpapiSecretStore());
            jar.Container.Add(new Uri("https://cookies.test"), new Cookie("jar", "jar-secret"));
            var store = new HistoryStore(new WindowsDpapiSecretStore(), directory);
            var values = new[] { "manual-secret", "jar-secret", "hop-secret", "final<secret", "second-secret" };
            var knownValues = fail ? values[..3] : values;
            var echoes = string.Join(" | ", knownValues.SelectMany(value => new[]
            {
                value, Uri.EscapeDataString(value), WebUtility.UrlEncode(value),
                JsonSerializer.Serialize(value)[1..^1]
            }));
            using var client = new HttpClient(new Handler(echoes, fail));
            var execution = new RequestExecutionService(new Factory(client), cookieJarService: jar);
            var request = new RequestItem { Url = "https://cookies.test/start", Body = "manual-secret" };
            request.Assertions.Add(new() { Kind = AssertionKind.Body, ExpectedValue = "manual-secret", Description = "jar-secret", Target = "hop-secret" });
            request.Headers.Add(new() { Key = "Cookie", Value = "session=manual-secret; preference=blue" });
            var tab = RequestTabViewModel.FromRequest(request, execution, onHistoryRecorded: store.Add);

            await tab.SendCommand.ExecuteAsync(null);
            await store.FlushAsync();

            var entry = Assert.Single(new HistoryStore(new WindowsDpapiSecretStore(), directory).GetAll());
            Assert.Equal(!fail, entry.IsSuccess);
            Assert.Contains("session=", entry.RequestSnapshot.Headers[0].Value);
            Assert.DoesNotContain("manual-secret", entry.RequestSnapshot.Body);
            if (!fail)
            {
                Assert.Contains("HttpOnly", entry.RedactedResponseHeadersText);
                Assert.Contains("Path=/", entry.RedactedResponseHeadersText);
                Assert.Contains("unrelated-public-data", entry.RedactedResponseBodyPreview);
            }
            foreach (var file in Directory.GetFiles(directory, "history.json*"))
            {
                var json = File.ReadAllText(file);
                foreach (var value in knownValues)
                {
                    Assert.DoesNotContain(value, json);
                    Assert.DoesNotContain(Uri.EscapeDataString(value), json);
                    Assert.DoesNotContain(WebUtility.UrlEncode(value), json);
                    Assert.DoesNotContain(JsonSerializer.Serialize(value)[1..^1].Replace("\\", "\\\\"), json);
                }
            }
            Assert.Equal("manual-secret", request.Body);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class Handler(string echoes, bool fail) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/start")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("/end", UriKind.Relative);
                redirect.Headers.TryAddWithoutValidation("Set-Cookie", "hop=hop-secret; Path=/; HttpOnly");
                return Task.FromResult(redirect);
            }
            if (fail) throw new HttpRequestException(echoes);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(echoes + " unrelated-public-data", Encoding.UTF8, "text/plain") };
            response.Headers.TryAddWithoutValidation("Set-Cookie", new[] { "final=final<secret; Path=/; HttpOnly", "second=second-secret; Secure" });
            response.Headers.TryAddWithoutValidation("X-Echo", echoes);
            return Task.FromResult(response);
        }
    }
}
