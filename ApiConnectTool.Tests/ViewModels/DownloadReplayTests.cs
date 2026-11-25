using System.Net;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class DownloadReplayTests
{
    [Fact]
    public async Task ConfirmedPostKeepsPreparedBodyAndRunsEachScriptOnlyOnce()
    {
        var bodies = new List<string>();
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(async message =>
        {
            bodies.Add(await message.Content!.ReadAsStringAsync());
            return new(HttpStatusCode.OK) { Content = new StringContent("response") };
        })));
        var workspace = new Workspace();
        var tab = RequestTabViewModel.FromRequest(new()
        {
            Method = HttpMethod.Post, Url = "https://example.test", Body = "{{value}}",
            PreRequestScript = "pm.globals.set('before', String(Number(pm.globals.get('before') || 0) + 1)); pm.variables.set('value', 'prepared-body');",
            TestScript = "pm.globals.set('after', String(Number(pm.globals.get('after') || 0) + 1)); pm.variables.set('value', 'test-body');"
        }, service);
        tab.WorkspaceProvider = () => workspace;
        var destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
        try
        {
            await tab.SendCommand.ExecuteAsync(null);
            tab.Method = "GET";
            tab.RequestBody = "edited-body";
            Assert.False(await tab.DownloadResponseAsync(destination, method =>
            {
                Assert.Equal("POST", method);
                return Task.FromResult(false);
            }));
            Assert.Single(bodies);
            Assert.True(await tab.DownloadResponseAsync(destination, method =>
            {
                Assert.Equal("POST", method);
                return Task.FromResult(true);
            }));
            Assert.Equal(new[] { "prepared-body", "prepared-body" }, bodies);
            Assert.All(workspace.GlobalVariables, variable => Assert.Equal("1", variable.CurrentValue.Value));
        }
        finally { File.Delete(destination); }
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task SafeMethodsDownloadWithoutMutationPrompt(string method)
    {
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))));
        var tab = new RequestTabViewModel(service) { Method = method, RequestUrl = "https://example.test" };
        var destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
        try
        {
            await tab.SendCommand.ExecuteAsync(null);
            Assert.True(await tab.DownloadResponseAsync(destination, _ => throw new InvalidOperationException("Unexpected mutation prompt")));
        }
        finally { File.Delete(destination); }
    }

    [Fact]
    public async Task StaleSignatureFailsVisiblyWithoutReplacingDestinationOrRerunningScripts()
    {
        var signatures = new List<string>();
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        {
            signatures.Add(message.Headers.GetValues("X-Signature").Single());
            return Task.FromResult(new HttpResponseMessage(signatures.Count == 1 ? HttpStatusCode.OK : HttpStatusCode.Forbidden)
                { Content = new StringContent("signature expired") });
        })));
        var workspace = new Workspace();
        var tab = RequestTabViewModel.FromRequest(new()
        {
            Url = "https://example.test", Headers = new() { new() { Key = "X-Signature", Value = "{{signature}}" } },
            PreRequestScript = "pm.globals.set('runs', String(Number(pm.globals.get('runs') || 0) + 1)); pm.variables.set('signature', 'signed-once');"
        }, service);
        tab.WorkspaceProvider = () => workspace;
        var destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
        try
        {
            await File.WriteAllTextAsync(destination, "existing file");
            await tab.SendCommand.ExecuteAsync(null);
            Assert.False(await tab.DownloadResponseAsync(destination));
            Assert.Contains("403", tab.ResponseActionMessage);
            Assert.Contains("fresh Send", tab.ResponseActionMessage);
            Assert.Equal("existing file", await File.ReadAllTextAsync(destination));
            Assert.Equal(new[] { "signed-once", "signed-once" }, signatures);
            Assert.Equal("1", workspace.GlobalVariables.Single(variable => variable.Key == "runs").CurrentValue.Value);
        }
        finally { File.Delete(destination); }
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("PROPFIND")]
    public async Task MutatingReplayRequiresExplicitConfirmation(string method)
    {
        var sends = 0;
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
        {
            sends++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        })));
        var tab = new RequestTabViewModel(service) { Method = method, RequestUrl = "https://example.test" };
        var destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
        try
        {
            await tab.SendCommand.ExecuteAsync(null);
            tab.Method = "GET";
            Assert.False(await tab.DownloadResponseAsync(destination));
            Assert.Equal(1, sends);
            Assert.False(File.Exists(destination));
        }
        finally { File.Delete(destination); }
    }

    [Fact]
    public async Task DecliningOriginalProductionEnvironmentSendsNothing()
    {
        var sends = 0;
        var promptedNames = new List<string>();
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
        {
            sends++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        })));
        var original = new RequestEnvironment { Name = "Original production", IsProduction = true };
        var selected = original;
        var tab = new RequestTabViewModel(service, () => selected, environment =>
        {
            promptedNames.Add(Assert.Single(environment).Environment.Name);
            return Task.FromResult(promptedNames.Count == 1);
        }) { RequestUrl = "https://example.test" };
        var destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
        try
        {
            await tab.SendCommand.ExecuteAsync(null);
            original.IsProduction = false;
            original.Name = "Renamed";
            selected = new() { Name = "Development" };
            Assert.False(await tab.DownloadResponseAsync(destination));
            Assert.Equal(new[] { "Original production", "Original production" }, promptedNames);
            Assert.Equal(1, sends);
            Assert.False(File.Exists(destination));
            Assert.False(tab.IsDownloading);
        }
        finally { File.Delete(destination); }
    }

    [Fact]
    public async Task DownloadKeepsGeneratedValuesDespiteTestScriptsEditsAndEnvironmentSwitch()
    {
        var requests = new List<string>();
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        {
            requests.Add($"{message.RequestUri} {message.Headers.GetValues("X-Value").Single()} {message.Headers.Authorization}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("complete bytes") });
        })));
        var selected = new RequestEnvironment { Variables = new() { new() { Key = "value", InitialValue = "initial" } } };
        var tab = RequestTabViewModel.FromRequest(new()
        {
            Url = "https://example.test/{{value}}", QueryParams = new() { new() { Key = "q", Value = "{{value}}" } },
            Headers = new() { new() { Key = "X-Value", Value = "{{value}}" } },
            Auth = new() { Type = AuthType.Bearer, Token = "{{value}}" },
            PreRequestScript = "pm.variables.set('value', 'generated');",
            TestScript = "pm.variables.set('value', 'after-test'); pm.environment.set('value', 'changed');"
        }, service, () => selected);
        var destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
        try
        {
            await tab.SendCommand.ExecuteAsync(null);
            tab.RequestUrl = "https://edited.test";
            tab.Auth.Token = "edited";
            tab.RequestHeaders[0].Value = "edited";
            selected = new() { Variables = new() { new() { Key = "value", InitialValue = "other-environment" } } };
            Assert.True(await tab.DownloadResponseAsync(destination));
            Assert.Equal(new[]
            {
                "https://example.test/generated?q=generated generated Bearer generated",
                "https://example.test/generated?q=generated generated Bearer generated"
            }, requests);
            Assert.Equal("complete bytes", await File.ReadAllTextAsync(destination));
        }
        finally { File.Delete(destination); }
    }
}
