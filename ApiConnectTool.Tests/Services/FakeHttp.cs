using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ApiConnectTool.Tests.Services;

// In-process HTTP fakes shared by service tests, so no test reaches a public endpoint or a
// loopback server just to observe a request or shape a response.
internal sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
}

internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return callback(request).WaitAsync(cancellationToken);
    }
}
