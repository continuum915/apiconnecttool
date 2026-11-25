using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class EffectiveAuthResolverTests
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [Fact]
    public async Task InheritedAuthSendsIdenticallyAndSharedOAuthUsesOneToken()
    {
        var tokenRequests = 0;
        var authorizations = new List<string>();
        var factory = new TestHttpClientFactory(new FakeHttpMessageHandler(request => {
            if (request.RequestUri!.AbsolutePath == "/token")
            {
                tokenRequests++;
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                    Content = new StringContent("""{"access_token":"shared-private","expires_in":3600}""") });
            }
            authorizations.Add(request.Headers.Authorization!.ToString());
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }));
        var cachePath = Path.Combine(Path.GetTempPath(), $"apiconnecttool-shared-{Guid.NewGuid():N}.json");
        try
        {
            var oauth = new OAuthTokenService(factory, new OAuthTokenCache(new WindowsDpapiSecretStore(), cachePath));
            var execution = new RequestExecutionService(factory, oauthTokenService: oauth);
            var collection = new RequestCollection { Auth = new() { Type = AuthType.OAuth2,
                GrantType = OAuthGrantType.ClientCredentials, ClientId = "client", TokenUrl = "https://example.test/token" },
                Requests = new() { new() { Url = "https://example.test/one" }, new() { Url = "https://example.test/two" } } };
            var tab = ApiConnectTool.ViewModels.RequestTabViewModel.FromRequest(collection.Requests[0], execution);
            tab.CollectionProvider = () => collection;
            await tab.SendCommand.ExecuteAsync(null);
            var report = await new CollectionRunner(new RequestRunner(execution)).RunAsync(collection);
            Assert.Equal(2, report.PassedCount);
            Assert.Equal(1, tokenRequests);
            Assert.Equal(new[] { "Bearer shared-private", "Bearer shared-private", "Bearer shared-private" }, authorizations);
        }
        finally { if (File.Exists(cachePath)) File.Delete(cachePath); }
    }
    [Fact]
    public void NearestParentWinsAndExplicitNoneStopsInheritance()
    {
        var collection = new RequestCollection { Name = "API", Auth = new() { Type = AuthType.Bearer, Token = "collection-token" } };
        var parent = new CollectionFolder { Name = "parent", Auth = new() { Type = AuthType.Basic, Username = "user" } };
        var child = new CollectionFolder { Name = "child", ParentFolderId = parent.Id };
        collection.Folders.Add(parent); collection.Folders.Add(child);
        var request = new RequestItem { FolderId = child.Id };
        var resolved = EffectiveAuthResolver.Resolve(request, collection);
        Assert.Same(parent.Auth, resolved.Auth);
        Assert.Equal("parent", resolved.SourceName);
        parent.Auth.Type = AuthType.Inherit;
        Assert.Same(collection.Auth, EffectiveAuthResolver.Resolve(request, collection).Auth);
        request.Auth.Type = AuthType.None;
        Assert.Equal(AuthType.None, EffectiveAuthResolver.Resolve(request, collection).Auth.Type);
        Assert.Equal(AuthType.None, EffectiveAuthResolver.Resolve(new(), null).Auth.Type);
        Assert.Equal(AuthType.Inherit, new CollectionFolder().Auth.Type);
        Assert.Throws<ArgumentException>(() => collection.Auth.Type = AuthType.Inherit);
    }
}
