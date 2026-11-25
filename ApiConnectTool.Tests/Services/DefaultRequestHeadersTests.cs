using System.Net;
using System.Reflection;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class DefaultRequestHeadersTests
{
    [Theory]
    [InlineData("User-Agent", "Custom/1", true)]
    [InlineData("Accept", "application/custom", true)]
    [InlineData("Accept-Encoding", "identity", true)]
    [InlineData("User-Agent", "Custom/1", false)]
    [InlineData("Accept", "application/custom", false)]
    [InlineData("Accept-Encoding", "identity", false)]
    public async Task EnabledCustomHeadersOverrideOnlyTheirOwnDefault(string name, string value, bool enabled)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        {
            foreach (var header in message.Headers) headers[header.Key] = string.Join(", ", header.Value);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        })));
        var request = new RequestItem { Url = "https://example.test", Headers = new() { new() { Key = name.ToLowerInvariant(), Value = value, IsEnabled = enabled } } };
        var result = await service.ExecuteAsync(request);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(enabled && name == "User-Agent" ? value : DefaultRequestHeaders.UserAgent, headers["User-Agent"]);
        Assert.Equal(enabled && name == "Accept" ? value : "*/*", headers["Accept"]);
        Assert.Equal(enabled && name == "Accept-Encoding" ? value : "gzip, deflate, br", headers["Accept-Encoding"]);
    }
    [Fact]
    public async Task IndividualSendsAndCollectionRunsUseDefaultsWithoutPersistingThem()
    {
        var sends = 0;
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        {
            sends++;
            Assert.Equal("ApiConnectTool/" + typeof(RequestExecutionService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
                Assert.Single(message.Headers.GetValues("User-Agent")));
            Assert.Equal("*/*", Assert.Single(message.Headers.GetValues("Accept")));
            Assert.Equal("gzip, deflate, br", string.Join(", ", message.Headers.GetValues("Accept-Encoding")));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        })));
        var request = new RequestItem { Url = "https://example.test" };
        Assert.True((await service.ExecuteAsync(request)).IsSuccess);
        var report = await new CollectionRunner(new RequestRunner(service)).RunAsync(new() { Requests = new() { request } });
        Assert.Equal(1, report.PassedCount);
        Assert.Equal(2, sends);
        Assert.Empty(request.Headers);
        var curl = new CurlGenerator().Generate(request, new Dictionary<string, string>(), false);
        Assert.DoesNotContain("User-Agent", curl);
        Assert.DoesNotContain("Accept", curl);
    }
}
