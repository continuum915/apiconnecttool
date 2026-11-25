using System.Net;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class DownloadReplayOAuthTests
{
    [Fact]
    public async Task DownloadRefreshesOnlyTheFrozenInheritedOAuthContext()
    {
        var directory = Path.Combine(Path.GetTempPath(), "apiconnecttool-replay-oauth-" + Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(directory, "response.bin");
        var tokenForms = new List<string>();
        var observed = new List<string>();
        var factory = new TestHttpClientFactory(new FakeHttpMessageHandler(async message =>
        {
            if (message.RequestUri!.AbsolutePath == "/token")
            {
                Assert.Equal("auth-original.test", message.RequestUri.Host);
                tokenForms.Add(await message.Content!.ReadAsStringAsync());
                return new(HttpStatusCode.OK) { Content = new StringContent("""{"access_token":"refreshed-original","expires_in":3600}""") };
            }
            observed.Add(message.Headers.Authorization!.ToString());
            return new(HttpStatusCode.OK) { Content = new StringContent("downloaded") };
        }));
        var secrets = new WindowsDpapiSecretStore();
        var cache = new OAuthTokenCache(secrets, Path.Combine(directory, "tokens.json"));
        var oauth = new OAuthTokenService(factory, cache);
        var environment = new RequestEnvironment { Name = "Original", Variables = new()
        {
            new() { Key = "host", InitialValue = "auth-original.test" },
            new() { Key = "client", InitialValue = "original-client" },
            new() { Key = "secret", InitialValue = "original-secret", IsSecret = true }
        } };
        var selected = environment;
        var parentAuth = new RequestAuth { Type = AuthType.OAuth2, GrantType = OAuthGrantType.ClientCredentials,
            TokenUrl = "https://{{host}}/token", ClientId = "{{client}}", ClientSecret = "{{secret}}", Scope = "{{scope}}" };
        var bound = parentAuth.Clone();
        bound.TokenUrl = "https://auth-original.test/token";
        bound.ClientId = "original-client";
        bound.ClientSecret = "original-secret";
        bound.Scope = "prepared-scope";
        bound.OAuthEnvironmentId = environment.Id;
        var request = new RequestItem { Url = "https://api.test/data", Auth = new() { Type = AuthType.Inherit },
            PreRequestScript = "pm.variables.set('scope', 'prepared-scope');",
            TestScript = "pm.environment.set('client', 'test-client'); pm.variables.set('scope', 'test-scope');" };
        var collection = new RequestCollection { Auth = parentAuth, Requests = new() { request } };
        var tab = RequestTabViewModel.FromRequest(request, new RequestExecutionService(factory, oauthTokenService: oauth), () => selected);
        tab.CollectionProvider = () => collection;
        try
        {
            await cache.SetAsync(bound.OAuthCacheKey, new("valid-original", "original-refresh", DateTimeOffset.UtcNow.AddHours(1))
                { Context = OAuthContext.From(bound) });
            await tab.SendCommand.ExecuteAsync(null);
            Assert.True(tab.LastSendSucceeded, tab.ResponseStatus);
            await cache.SetAsync(bound.OAuthCacheKey, new("expired-original", "original-refresh", DateTimeOffset.UtcNow.AddMinutes(-1))
                { Context = OAuthContext.From(bound) });
            parentAuth.TokenUrl = "https://edited.test/token";
            parentAuth.ClientSecret = "edited-secret";
            selected = new() { Name = "Other environment" };
            Assert.True(await tab.DownloadResponseAsync(destination), tab.ResponseActionMessage);
            var form = Assert.Single(tokenForms);
            Assert.Contains("grant_type=refresh_token", form);
            Assert.Contains("refresh_token=original-refresh", form);
            Assert.Contains("client_id=original-client", form);
            Assert.Contains("client_secret=original-secret", form);
            Assert.DoesNotContain("test-client", form);
            Assert.Equal(new[] { "Bearer valid-original", "Bearer refreshed-original" }, observed);
            Assert.Equal("downloaded", await File.ReadAllTextAsync(destination));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ResolvedReplayCredentialsDoNotEnterWorkspaceHistoryOrSessionFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "apiconnecttool-replay-secrets-" + Guid.NewGuid().ToString("N"));
        var secrets = new WindowsDpapiSecretStore();
        var workspace = new WorkspaceService(secrets, directory);
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        {
            Assert.Equal("Bearer private-seed-computed", message.Headers.Authorization!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("private-seed-computed") });
        })));
        try
        {
            var vm = new MainWindowViewModel(workspace, service, historyStore: new HistoryStore(secrets, directory), sessionStore: new SessionStore(directory));
            vm.Environments.Add(new() { Variables = new() { new() { Key = "seed", InitialValue = "private-seed", IsSecret = true } } });
            vm.SelectedEnvironment = vm.Environments[0];
            vm.AddTabCommand.Execute(null);
            var tab = vm.ActiveTab!;
            tab.RequestUrl = "https://example.test";
            tab.Auth = new() { Type = AuthType.Bearer, Token = "{{computed}}" };
            tab.AssertionTests.PreRequestScript = "pm.variables.set('computed', pm.environment.get('seed') + '-computed');";
            await vm.SaveCommand.ExecuteAsync(null);
            await tab.SendCommand.ExecuteAsync(null);
            Assert.True(tab.LastSendSucceeded, tab.ResponseStatus);
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.True(await tab.DownloadResponseAsync(Path.Combine(directory, "response.bin")));
            await vm.History.FlushAsync(TimeSpan.FromSeconds(5));
            foreach (var path in Directory.GetFiles(directory).Where(path => !path.EndsWith("response.bin")))
            {
                var persisted = await File.ReadAllTextAsync(path);
                Assert.DoesNotContain("private-seed", persisted);
                Assert.DoesNotContain("private-seed-computed", persisted);
            }
            var reopened = new MainWindowViewModel(workspace, service, sessionStore: new SessionStore(directory));
            Assert.False(reopened.ActiveTab!.CanDownloadResponse);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
