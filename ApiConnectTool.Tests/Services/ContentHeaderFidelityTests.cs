using System.Net;
using System.Net.Http.Headers;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class ContentHeaderFidelityTests
{
    private static (RequestExecutionService Service, List<HttpRequestMessage> Sent, List<string> Bodies) Capture(
        Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        var sent = new List<HttpRequestMessage>();
        var bodies = new List<string>();
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(async message =>
        {
            sent.Add(message);
            bodies.Add(message.Content is null ? string.Empty : await message.Content.ReadAsStringAsync());
            return respond?.Invoke(message) ?? new HttpResponseMessage(HttpStatusCode.OK);
        })));
        return (service, sent, bodies);
    }

    private static RequestItem FormDataRequest(params (string Key, string Value)[] headers) => new()
    {
        Method = HttpMethod.Post,
        Url = "https://example.test/upload",
        BodyMode = RequestBodyMode.FormData,
        FormData = new() { new() { Key = "field", Value = "value" } },
        Headers = new(headers.Select(h => new KeyValueItem { Key = h.Key, Value = h.Value }))
    };

    [Theory]
    [InlineData("multipart/form-data")]
    [InlineData("multipart/form-data; boundary=user-supplied")]
    public async Task FormDataWithUserMultipartContentTypeSendsTheGeneratedBoundary(string userContentType)
    {
        var (service, sent, bodies) = Capture();

        var result = await service.ExecuteAsync(FormDataRequest(("Content-Type", userContentType)), new());

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var contentType = Assert.Single(sent).Content!.Headers.ContentType!;
        Assert.Equal("multipart/form-data", contentType.MediaType);
        var boundary = contentType.Parameters.Single(p => p.Name == "boundary").Value!.Trim('"');
        Assert.NotEqual("user-supplied", boundary);
        Assert.StartsWith("--" + boundary, Assert.Single(bodies));
    }

    [Fact]
    public async Task FormDataKeepsTheUserMultipartMediaTypeAndOtherParameters()
    {
        var (service, sent, bodies) = Capture();

        var result = await service.ExecuteAsync(FormDataRequest(("Content-Type", "multipart/mixed; charset=utf-8; boundary=mine")), new());

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var contentType = Assert.Single(sent).Content!.Headers.ContentType!;
        Assert.Equal("multipart/mixed", contentType.MediaType);
        Assert.Equal("utf-8", contentType.CharSet);
        var boundary = Assert.Single(contentType.Parameters, p => p.Name == "boundary").Value!.Trim('"');
        Assert.NotEqual("mine", boundary);
        Assert.StartsWith("--" + boundary, Assert.Single(bodies));
    }

    [Theory]
    [InlineData(RequestBodyMode.FormData, "text/plain")]
    [InlineData(RequestBodyMode.Raw, "multipart/form-data; boundary=raw-author")]
    [InlineData(RequestBodyMode.UrlEncoded, "application/x-www-form-urlencoded; charset=latin1")]
    public async Task OtherBodiesHonorTheUserContentTypeExactly(RequestBodyMode mode, string userContentType)
    {
        var (service, sent, _) = Capture();
        var request = FormDataRequest(("Content-Type", userContentType));
        request.BodyMode = mode;
        request.Body = "raw body";

        var result = await service.ExecuteAsync(request, new());

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(MediaTypeHeaderValue.Parse(userContentType), Assert.Single(sent).Content!.Headers.ContentType);
    }

    private static RequestItem RawRequest(params (string Key, string Value)[] headers)
    {
        var request = FormDataRequest(headers);
        request.BodyMode = RequestBodyMode.Raw;
        request.Body = "payload";
        return request;
    }

    [Theory]
    [InlineData("Content-Encoding", "gzip")]
    [InlineData("Content-Language", "de-DE")]
    [InlineData("Content-Disposition", "attachment; filename=\"report.txt\"")]
    [InlineData("Expires", "Thu, 01 Dec 2030 16:00:00 GMT")]
    public async Task ContentHeadersAreSentWithTheBody(string name, string value)
    {
        var (service, sent, _) = Capture();

        var result = await service.ExecuteAsync(RawRequest((name, value)), new());

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(value, string.Join(", ", Assert.Single(sent).Content!.Headers.GetValues(name)));
    }

    [Theory]
    [InlineData("Content-Encoding", "gzip")]
    [InlineData("Content-Type", "application/json")]
    public async Task AContentHeaderWithoutABodyIsAValidationErrorNamingTheHeader(string name, string value)
    {
        var (service, sent, _) = Capture();
        var request = RawRequest((name, value));
        request.Method = HttpMethod.Get;

        var result = await service.ExecuteAsync(request, new());

        Assert.Equal(RequestExecutionFailureKind.Validation, result.FailureKind);
        Assert.Contains(name, result.ErrorMessage);
        Assert.Contains("body", result.ErrorMessage);
        Assert.Empty(sent);
    }

    [Theory]
    [InlineData(RequestBodyMode.Raw)]
    [InlineData(RequestBodyMode.None)]
    public async Task AUserContentLengthIsAValidationErrorExplainingItIsComputed(RequestBodyMode mode)
    {
        var (service, sent, _) = Capture();
        var request = RawRequest(("Content-Length", "7"));
        request.BodyMode = mode;

        var result = await service.ExecuteAsync(request, new());

        Assert.Equal(RequestExecutionFailureKind.Validation, result.FailureKind);
        Assert.Contains("Content-Length", result.ErrorMessage);
        Assert.Contains("computed", result.ErrorMessage);
        Assert.Empty(sent);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> RedirectOnce(HttpStatusCode status, string location)
    {
        var redirected = false;
        return _ =>
        {
            if (redirected) return new HttpResponseMessage(HttpStatusCode.OK);
            redirected = true;
            var response = new HttpResponseMessage(status);
            response.Headers.Location = new Uri(location);
            return response;
        };
    }

    [Fact]
    public async Task ARedirectThatDropsTheBodySkipsContentHeaders()
    {
        var (service, sent, _) = Capture(RedirectOnce(HttpStatusCode.SeeOther, "https://example.test/result"));

        var result = await service.ExecuteAsync(RawRequest(("Content-Encoding", "gzip")), new() { FollowRedirects = true });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(2, sent.Count);
        Assert.Null(sent[1].Content);
        Assert.Equal(HttpMethod.Get, sent[1].Method);
    }

    [Theory]
    [InlineData("https://example.test/next", true)]
    [InlineData("https://other.test/next", false)]
    public async Task ContentHeadersCarryingSecretsFollowOnlySameOriginRedirects(string location, bool retained)
    {
        var (service, sent, _) = Capture(RedirectOnce(HttpStatusCode.TemporaryRedirect, location));
        var request = RawRequest(("Content-Disposition", "attachment; filename=\"{{secret}}.txt\""), ("Content-Language", "de-DE"));

        var result = await service.ExecuteAsync(request, new()
        {
            FollowRedirects = true,
            Variables = new Dictionary<string, string> { ["secret"] = "private-value" },
            SecretVariableNames = new HashSet<string> { "secret" }
        });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(2, sent.Count);
        var hop = sent[1].Content!.Headers;
        Assert.Equal(retained, hop.Contains("Content-Disposition"));
        Assert.Equal("de-DE", string.Join(", ", hop.GetValues("Content-Language")));
    }
}
