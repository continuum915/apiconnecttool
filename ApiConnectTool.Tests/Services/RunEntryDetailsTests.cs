using System.Net;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class RunEntryDetailsTests
{
    private const string Secret = "tok-123-secret";

    private static async Task<CollectionRunEntry> RunOnce(Func<HttpRequestMessage, HttpResponseMessage> respond, RequestItem request)
    {
        var runner = new CollectionRunner(RequestRunnerTests.Runner(message => Task.FromResult(respond(message))));
        var environment = new RequestEnvironment
        {
            Name = "dev",
            Variables = new() { new() { Key = "token", InitialValue = Secret, IsSecret = true } }
        };
        var collection = new RequestCollection { Name = "Api", Requests = { request } };
        var report = await ((ICollectionRunner)runner).RunAsync(collection, new CollectionRunConfiguration { Environment = environment });
        return Assert.Single(report.Entries);
    }

    [Fact]
    public async Task Details_KeepTheRedactedRequestResponseAndConsole()
    {
        var entry = await RunOnce(message => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Headers = { { "X-Echo", message.Headers.GetValues("X-Token").Single() } },
            Content = new StringContent("{\"token\":\"" + Secret + "\"}", System.Text.Encoding.UTF8, "application/json")
        }, new RequestItem
        {
            Name = "Echo", Method = HttpMethod.Post, Url = "https://example.test/items?key={{token}}",
            Headers = new() { new() { Key = "X-Token", Value = "{{token}}" } },
            TestScript = "console.log('token ' + pm.environment.get('token'));"
        });

        var details = Assert.IsType<RunEntryDetails>(entry.Details);
        Assert.Equal("POST", details.Method);
        Assert.StartsWith("https://example.test/items?key=", details.RequestUrl);
        Assert.Contains("X-Token: ", details.RequestHeaders);
        Assert.Contains("X-Echo: ", details.ResponseHeaders);
        Assert.Contains("\"token\"", details.ResponseBody);
        Assert.False(details.ResponseBodyTruncated);
        Assert.StartsWith("token ", Assert.Single(details.Console).Text);
        foreach (var text in new[] { details.RequestUrl, details.RequestHeaders, details.ResponseHeaders, details.ResponseBody, details.Console[0].Text })
            Assert.DoesNotContain(Secret, text);
        Assert.Contains(SecretRedactor.Placeholder, details.RequestHeaders);
    }

    [Fact]
    public async Task ResponseBodyPreview_IsCappedLikeHistory()
    {
        var body = new string('a', ResponsePreview.MaxBodyPreviewBytes + 500);
        var entry = await RunOnce(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "text/plain")
        }, new RequestItem { Name = "Big", Url = "https://example.test/big" });

        Assert.True(entry.Details!.ResponseBodyTruncated);
        Assert.Equal(ResponsePreview.MaxBodyPreviewBytes, entry.Details.ResponseBody.Length);
    }

    [Fact]
    public async Task TransportErrors_StillKeepTheRequestThatWasSent()
    {
        var entry = await RunOnce(_ => throw new HttpRequestException("refused"),
            new RequestItem { Name = "Down", Url = "https://example.test/down?key={{token}}" });
        Assert.Equal(RunEntryOutcome.Errored, entry.Outcome);
        Assert.StartsWith("https://example.test/down", entry.Details!.RequestUrl);
        Assert.DoesNotContain(Secret, entry.Details.RequestUrl);
        Assert.Equal(string.Empty, entry.Details.ResponseBody);
    }
}
