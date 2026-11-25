using System.Net;
using System.Text.Json;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class DerivedSecretTests
{
    [Theory]
    [InlineData("password", "dXNlcjpwYXNzd29yZA==")]
    [InlineData("p& ss", "p%26%20ss")]
    [InlineData("a\"b\\c", "a\\\"b\\\\c")]
    [InlineData("p&ss", "p\\u0026ss")]
    [InlineData("p& ss", "p%26+ss")]
    public async Task DerivedCredentials_AreHiddenAcrossHistoryCurlScriptsAndReports(string password, string echoed)
    {
        var execution = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(echoed) };
            response.Headers.Add("X-Echo", echoed);
            return Task.FromResult(response);
        })));
        var request = new RequestItem
        {
            Url = "https://example.test", Name = echoed, Method = HttpMethod.Post,
            Auth = new() { Type = AuthType.Basic, Username = "user", Password = new(password) },
            Body = echoed,
            TestScript = "pm.test(pm.response.text(), () => { throw new Error(pm.response.text()); });"
        };
        HistoryEntry? history = null;
        var tab = RequestTabViewModel.FromRequest(request, execution, onHistoryRecorded: entry => history = entry);
        await tab.SendCommand.ExecuteAsync(null);
        Assert.NotNull(history);
        Assert.DoesNotContain(echoed, history.RedactedResponseBodyPreview);
        Assert.DoesNotContain(echoed, history.RedactedResponseHeadersText);

        var curl = new CurlGenerator();
        Assert.Contains(echoed, curl.Generate(request, new Dictionary<string, string>(), true));
        Assert.DoesNotContain(echoed, curl.Generate(request, new Dictionary<string, string>(), false));

        var runner = new RequestRunner(execution);
        var run = await runner.RunAsync(request);
        var script = Assert.Single(run.Scripts);
        Assert.DoesNotContain(echoed, Assert.Single(script.Tests).Label);
        Assert.DoesNotContain(echoed, Assert.Single(script.Tests).FailureMessage!);
        var report = await new CollectionRunner(runner).RunAsync(new() { Requests = new() { request } });
        Assert.DoesNotContain(echoed, JsonSerializer.Serialize(report));
        Assert.Contains("••••••••", Assert.Single(report.Entries).RequestName);
    }

    [Fact]
    public async Task BasicCredential_UsesResolvedUsernameAndPasswordIncludingForDownloadFailures()
    {
        const string credential = "dXNlcjpwYXNzd29yZA==";
        var execution = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        {
            Assert.Equal(credential, message.Headers.Authorization!.Parameter);
            throw new HttpRequestException(credential);
        })));
        var request = new RequestItem { Url = "https://example.test",
            Auth = new() { Type = AuthType.Basic, Username = "{{user}}", Password = new("{{pass}}") } };
        var options = new RequestExecutionOptions { Variables = new Dictionary<string, string>
            { ["user"] = "user", ["pass"] = "password" } };
        var result = await execution.ExecuteAsync(request, options);
        Assert.False(result.IsSuccess);
        Assert.Equal("••••••••", new SecretRedactor().Redact(result.ErrorMessage!, result.ResolvedSecretValues));
        var download = await execution.DownloadAsync(request, Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".download"), options);
        Assert.False(download.IsSuccess);
        Assert.Equal("••••••••", new SecretRedactor().Redact(download.ErrorMessage!, download.ResolvedSecretValues));
    }

    [Theory]
    [InlineData("p& ss", "p%26%20ss")]
    [InlineData("a\"b\\c", "a\\\"b\\\\c")]
    public async Task SecretVariables_AreHiddenWithoutAnAuthBlock(string secret, string echoed)
    {
        var execution = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(echoed) }))));
        var environment = new RequestEnvironment { Variables = new() {
            new() { Key = "secret", InitialValue = new(secret), IsSecret = true } } };
        var request = new RequestItem { Url = "https://example.test", Method = HttpMethod.Post,
            Body = "{{secret}}", TestScript = "throw new Error(pm.response.text());" };
        HistoryEntry? history = null;
        var tab = RequestTabViewModel.FromRequest(request, execution, environmentProvider: () => environment,
            onHistoryRecorded: entry => history = entry);
        await tab.SendCommand.ExecuteAsync(null);
        Assert.Equal("••••••••", history!.RedactedResponseBodyPreview);
        request.Body = echoed;
        var curlTab = RequestTabViewModel.FromRequest(request, execution, environmentProvider: () => environment);
        Assert.Contains(echoed, curlTab.BuildCurlCommand(true));
        Assert.DoesNotContain(echoed, curlTab.BuildCurlCommand(false));
        var report = await new CollectionRunner(new RequestRunner(execution)).RunAsync(
            new() { Requests = new() { request } }, environment);
        Assert.Equal(RunEntryOutcome.Errored, Assert.Single(report.Entries).Outcome);
        Assert.DoesNotContain(echoed, Assert.Single(report.Entries).ErrorMessage!);
    }
}
