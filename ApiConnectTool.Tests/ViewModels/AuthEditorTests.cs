using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class AuthEditorTests
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [Fact]
    public async Task ParentOAuthAuthorizationSharesCacheAndRedactsInheritedFailure()
    {
        var cachePath = Path.Combine(Path.GetTempPath(), $"apiconnecttool-auth-editor-{Guid.NewGuid():N}.json");
        try
        {
            var calls = 0;
            var factory = new ApiConnectTool.Tests.Services.TestHttpClientFactory(new ApiConnectTool.Tests.Services.FakeHttpMessageHandler(_ => {
                calls++;
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                    Content = new StringContent("""{"access_token":"editor-token","expires_in":3600}""") });
            }));
            var oauth = new ApiConnectTool.Services.OAuthTokenService(factory, new ApiConnectTool.Services.OAuthTokenCache(new ApiConnectTool.Services.WindowsDpapiSecretStore(), cachePath));
            var collection = new RequestCollection { Auth = new() { Type = AuthType.OAuth2, GrantType = OAuthGrantType.ClientCredentials,
                ClientId = "client", ClientSecret = "parent-private", TokenUrl = "https://example.test/token" } };
            var settings = new CollectionSettingsViewModel(collection, () => { }, oauth);
            await settings.AuthEditor.AuthorizeCommand.ExecuteAsync(null);
            Assert.Equal("editor-token", await oauth.GetValidAccessTokenAsync(collection.Auth.Clone()));
            Assert.Equal(1, calls);
            var folder = new CollectionFolder { Auth = collection.Auth.Clone(false) }; collection.Folders.Add(folder);
            var folderSettings = new FolderSettingsViewModel(folder, collection, () => { }, oauth);
            await folderSettings.AuthEditor.AuthorizeCommand.ExecuteAsync(null);
            Assert.Equal("editor-token", await oauth.GetValidAccessTokenAsync(folder.Auth.Clone()));
            Assert.Equal(2, calls);
            var failingFactory = new ApiConnectTool.Tests.Services.TestHttpClientFactory(new ApiConnectTool.Tests.Services.FakeHttpMessageHandler(_ =>
                Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest) { Content = new StringContent("parent-private") })));
            var failing = new ApiConnectTool.Services.OAuthTokenService(failingFactory, new ApiConnectTool.Services.OAuthTokenCache(new ApiConnectTool.Services.WindowsDpapiSecretStore(), cachePath));
            collection.Auth.OAuthCacheKey = Guid.NewGuid().ToString("N");
            var child = new AuthEditorViewModel(new() { Type = AuthType.Inherit }, failing,
                () => new(collection.Auth, collection.Name));
            await child.AuthorizeCommand.ExecuteAsync(null);
            Assert.NotNull(child.AuthorizationMessage);
            Assert.DoesNotContain("parent-private", child.AuthorizationMessage);
            child.Dispose(); settings.FlushChanges(); folderSettings.FlushChanges();
        }
        finally { if (File.Exists(cachePath)) File.Delete(cachePath); }
    }
    [Fact]
    public void ParentAuthChangesAndMovesUpdateSourceLabel()
    {
        var collection = new RequestCollection { Name = "API" };
        var request = new RequestItem(); collection.Requests.Add(request);
        var tab = RequestTabViewModel.FromRequest(request);
        tab.CollectionProvider = () => collection;
        tab.RefreshAuthContext();
        Assert.Equal("No auth is set on any parent", tab.AuthEditor.InheritanceLabel);
        collection.Auth.Type = AuthType.Bearer;
        Assert.Equal("Using Bearer Token from API", tab.AuthEditor.InheritanceLabel);
        var folder = new CollectionFolder { Name = "Folder", Auth = new() { Type = AuthType.Basic } };
        collection.Folders.Add(folder); request.FolderId = folder.Id;
        tab.RefreshAuthContext();
        Assert.Equal("Using Basic Auth from Folder", tab.AuthEditor.InheritanceLabel);
        var settings = new CollectionSettingsViewModel(collection, () => { });
        Assert.DoesNotContain(settings.AuthEditor.AuthChoices, choice => choice.Type == AuthType.Inherit);
        var folderSettings = new FolderSettingsViewModel(folder, collection, () => { });
        Assert.Equal(AuthType.Inherit, folderSettings.AuthEditor.AuthChoices[0].Type);
        folderSettings.AuthEditor.Auth.Token = "changed";
        Assert.Equal("changed", folder.Auth.Token.Value);
    }
}
