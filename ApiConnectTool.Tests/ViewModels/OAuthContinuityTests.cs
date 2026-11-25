using System.Net;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class OAuthContinuityTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "apiconnecttool-continuity-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _credentials = new();
    private int _exchanges;
    private readonly List<string> _forms = new();
    private readonly List<HistoryStore> _histories = new();

    private MainWindowViewModel Open()
    {
        var secrets = new WindowsDpapiSecretStore();
        var factory = new TestHttpClientFactory(new FakeHttpMessageHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/token")
            {
                _forms.Add(await request.Content!.ReadAsStringAsync());
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                    "{\"access_token\":\"issued-" + ++_exchanges + "\",\"expires_in\":3600}") };
            }
            _credentials.Add(request.Headers.Authorization?.ToString() ?? "missing");
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var oauth = new OAuthTokenService(factory, new OAuthTokenCache(secrets, Path.Combine(_directory, "tokens.json")));
        return new(new WorkspaceService(secrets, _directory), new RequestExecutionService(factory, oauthTokenService: oauth),
            oauth, Track(new HistoryStore(secrets, _directory)), sessionStore: new SessionStore(_directory));
    }

    private HistoryStore Track(HistoryStore history)
    {
        _histories.Add(history);
        return history;
    }

    private static RequestAuth Auth() => new() { Type = AuthType.OAuth2, GrantType = OAuthGrantType.ClientCredentials,
        TokenUrl = "https://api.test/token", ClientId = "client", ClientSecret = "secret" };

    private async Task Send(MainWindowViewModel vm)
    {
        await vm.ActiveTab!.SendCommand.ExecuteAsync(null);
        Assert.True(vm.ActiveTab.LastSendSucceeded, vm.ActiveTab.ResponseStatus);
    }

    [Fact]
    public async Task SavedRequestReusesAuthorizedTokenAfterEditingHistoryAndSessionReopening()
    {
        var vm = Open();
        vm.AddTabCommand.Execute(null);
        vm.ActiveTab!.RequestUrl = "https://api.test/data";
        vm.ActiveTab.Auth = Auth();
        await vm.SaveCommand.ExecuteAsync(null);
        await vm.ActiveTab.AuthEditor.AuthorizeCommand.ExecuteAsync(null);
        await Send(vm);
        vm.ActiveTab.RequestBody = "edited";
        await vm.SaveCommand.ExecuteAsync(null);
        var saved = vm.CollectionsPanel.Collections.Single().Requests.Single();
        await vm.CloseTabCommand.ExecuteAsync(vm.ActiveTab);
        vm.OpenRequestCommand.Execute(saved);
        await Send(vm);
        await vm.History.FlushAsync(TimeSpan.FromSeconds(5));
        vm.History.ResendCommand.Execute(new HistoryStore(new WindowsDpapiSecretStore(), _directory).GetAll().First());
        await Send(vm);
        var restored = Open();
        await Send(restored);
        Assert.Equal(1, _exchanges);
        Assert.Equal(Enumerable.Repeat("Bearer issued-1", 4), _credentials);
    }

    [Fact]
    public async Task SaveAsDuplicateAndImportAuthorizeIndependentlyWithoutChangingOriginal()
    {
        var vm = Open();
        vm.AddTabCommand.Execute(null);
        vm.ActiveTab!.RequestUrl = "https://api.test/data";
        vm.ActiveTab.Auth = Auth();
        await vm.SaveCommand.ExecuteAsync(null);
        var collection = vm.CollectionsPanel.Collections.Single();
        var original = collection.Requests.Single();
        await vm.ActiveTab.AuthEditor.AuthorizeCommand.ExecuteAsync(null);
        await vm.SaveAsCommand.ExecuteAsync(null);
        await Send(vm);
        await vm.SaveCommand.ExecuteAsync(null);
        await vm.CloseTabCommand.ExecuteAsync(vm.ActiveTab);
        vm.OpenRequestCommand.Execute(collection.Requests.Last());
        await Send(vm);
        await vm.CollectionsPanel.DuplicateRequestCommand.ExecuteAsync(original);
        vm.OpenRequestCommand.Execute(collection.Requests[1]);
        await Send(vm);
        var exports = new CollectionExportService();
        var imported = exports.Import(exports.Export(collection, Guid.NewGuid(), true));
        vm.CollectionsPanel.Collections.Add(imported);
        vm.OpenRequestCommand.Execute(imported.Requests.First());
        await Send(vm);
        vm.OpenRequestCommand.Execute(original);
        await Send(vm);
        Assert.Equal(4, _exchanges);
        Assert.Equal(new[] { "Bearer issued-2", "Bearer issued-2", "Bearer issued-3", "Bearer issued-4", "Bearer issued-1" }, _credentials);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChildrenAndCopiesShareCompatibleParentAuthorization(bool folderAuth)
    {
        var collection = new RequestCollection { Name = "API" };
        var folder = new CollectionFolder { Name = "Folder" };
        collection.Folders.Add(folder);
        if (folderAuth) folder.Auth = Auth();
        else collection.Auth = Auth();
        collection.Requests.Add(new() { Name = "First", Url = "https://api.test/first", FolderId = folder.Id });
        collection.Requests.Add(new() { Name = "Second", Url = "https://api.test/second", FolderId = folder.Id });
        var workspace = new Workspace();
        workspace.Collections.Add(collection);
        new WorkspaceService(new WindowsDpapiSecretStore(), _directory).SaveWorkspace(workspace);
        var vm = Open();
        collection = vm.CollectionsPanel.Collections.Single();
        if (folderAuth) await vm.CreateFolderSettings(collection.Folders.Single()).AuthEditor.AuthorizeCommand.ExecuteAsync(null);
        else await vm.CreateCollectionSettings(collection).AuthEditor.AuthorizeCommand.ExecuteAsync(null);
        foreach (var request in collection.Requests.ToArray())
        {
            vm.OpenRequestCommand.Execute(request);
            await Send(vm);
        }
        await vm.SaveAsCommand.ExecuteAsync(null);
        await Send(vm);
        await Send(Open());
        Assert.Equal(1, _exchanges);
        Assert.Equal(Enumerable.Repeat("Bearer issued-1", 4), _credentials);
    }

    [Fact]
    public async Task InteractiveTokenSurvivesReopeningButCopyRequiresAuthorization()
    {
        var auth = Auth();
        auth.GrantType = OAuthGrantType.AuthorizationCodePkce;
        var request = new RequestItem { Name = "Interactive", Url = "https://api.test/data", Auth = auth };
        var collection = new RequestCollection();
        collection.Requests.Add(request);
        var workspace = new Workspace();
        workspace.Collections.Add(collection);
        var secrets = new WindowsDpapiSecretStore();
        new WorkspaceService(secrets, _directory).SaveWorkspace(workspace);
        // Model the successful browser consent at the public token-cache boundary.
        await new OAuthTokenCache(secrets, Path.Combine(_directory, "tokens.json")).SetAsync(auth.OAuthCacheKey,
            new("interactive-token", null, DateTimeOffset.UtcNow.AddHours(1)) { Context = OAuthContext.From(auth) });
        var vm = Open();
        vm.OpenRequestCommand.Execute(vm.CollectionsPanel.Collections.Single().Requests.Single());
        await Send(vm);
        await vm.SaveAsCommand.ExecuteAsync(null);
        await vm.ActiveTab!.SendCommand.ExecuteAsync(null);
        Assert.False(vm.ActiveTab.LastSendSucceeded);
        Assert.Equal(0, _exchanges);
        Assert.Equal(new[] { "Bearer interactive-token" }, _credentials);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task AuthorizeAndSendShareResolvedScopePrecedence(bool inherited, bool parentSettings, bool folderAuth)
    {
        var auth = Auth();
        auth.ClientId = "{{client}}";
        auth.ClientSecret = "{{secret}}";
        auth.TokenUrl = "{{endpoint}}";
        auth.Scope = "{{scope}}";
        var collection = new RequestCollection();
        if (inherited) collection.Auth = auth;
        var folder = new CollectionFolder { Auth = auth };
        if (folderAuth)
        {
            collection.Auth = new();
            collection.Folders.Add(folder);
        }
        collection.Variables.Add(new() { Key = "client", InitialValue = "collection-client" });
        collection.Variables.Add(new() { Key = "scope", InitialValue = "collection-scope" });
        collection.Requests.Add(new() { Url = "https://api.test/data", FolderId = folderAuth ? folder.Id : null,
            Auth = inherited ? new() { Type = AuthType.Inherit } : auth });
        var workspace = new Workspace();
        workspace.Collections.Add(collection);
        workspace.GlobalVariables.Add(new() { Key = "client", InitialValue = "global-client" });
        workspace.GlobalVariables.Add(new() { Key = "scope", InitialValue = "global-scope" });
        workspace.GlobalVariables.Add(new() { Key = "endpoint", InitialValue = "https://api.test/token" });
        var environment = new RequestEnvironment();
        environment.Variables.Add(new() { Key = "client", InitialValue = "environment-default", CurrentValue = "environment-client" });
        environment.Variables.Add(new() { Key = "secret", InitialValue = "resolved secret&", IsSecret = true });
        workspace.Environments.Add(environment);
        new WorkspaceService(new WindowsDpapiSecretStore(), _directory).SaveWorkspace(workspace);
        var vm = Open();
        vm.SelectedEnvironment = vm.Environments.Single();
        vm.OpenRequestCommand.Execute(vm.CollectionsPanel.Collections.Single().Requests.Single());
        collection = vm.CollectionsPanel.Collections.Single();
        var editor = parentSettings ? folderAuth ? vm.CreateFolderSettings(collection.Folders.Single()).AuthEditor
            : vm.CreateCollectionSettings(collection).AuthEditor : vm.ActiveTab!.AuthEditor;
        await editor.AuthorizeCommand.ExecuteAsync(null);
        Assert.Equal(1, _exchanges);
        await Send(vm);
        Assert.Equal(1, _exchanges);
        Assert.Equal("grant_type=client_credentials&client_id=environment-client&client_secret=resolved+secret%26&scope=collection-scope", Assert.Single(_forms));
        Assert.Equal("Bearer issued-1", Assert.Single(_credentials));
        Assert.Equal("{{client}}", inherited ? folderAuth ? collection.Folders.Single().Auth.ClientId : collection.Auth.ClientId : vm.ActiveTab!.Auth.ClientId);
    }

    public void Dispose()
    {
        Task.WaitAll(_histories.Select(history => history.FlushAsync()));
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
