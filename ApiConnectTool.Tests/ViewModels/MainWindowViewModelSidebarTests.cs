using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

/// <summary>Characterization of sidebar collection behavior (deletes, tree, search, duplicate,
/// move), pinned before the extraction and now driven through the collections view model's
/// commands. Kept at the main window so it checks the seam end to end: tabs closed or
/// retitled, workspace saves, and the save error.</summary>
public class MainWindowViewModelSidebarTests
{
    private readonly RequestCollection _api = new() { Name = "My API" };
    private readonly RequestCollection _auth = new() { Name = "Auth" };
    private readonly CollectionFolder _users = new() { Name = "Users", SortOrder = 0 };
    private readonly CollectionFolder _admin;
    private readonly FakeWorkspaceService _service;
    private readonly MainWindowViewModel _vm;

    public MainWindowViewModelSidebarTests()
    {
        _admin = new CollectionFolder { Name = "Admin", ParentFolderId = _users.Id };
        _api.Folders.Add(_users);
        _api.Folders.Add(_admin);
        _service = new FakeWorkspaceService
        {
            WorkspaceToReturn = new Workspace { Collections = new ObservableCollection<RequestCollection> { _api, _auth } }
        };
        _vm = new MainWindowViewModel(_service);
        _vm.CollectionsPanel.DeleteConfirmationHandler = (_, _) => Task.FromResult(true);
    }

    private RequestItem AddRequest(RequestCollection collection, string name, Guid? folderId = null)
    {
        var request = new RequestItem
        {
            Name = name,
            Url = $"https://api.example.test/{name}",
            FolderId = folderId,
            SortOrder = collection.Requests.Count(r => r.FolderId == folderId)
        };
        collection.Requests.Add(request);
        return request;
    }

    private static List<string> Names(RequestCollection collection, Guid? folderId) =>
        collection.Requests.Where(r => r.FolderId == folderId).OrderBy(r => r.SortOrder).Select(r => r.Name).ToList();

    private static List<int> SortOrders(RequestCollection collection, Guid? folderId) =>
        collection.Requests.Where(r => r.FolderId == folderId).OrderBy(r => r.SortOrder).Select(r => r.SortOrder).ToList();

    private CollectionTreeNode TreeNode(RequestCollection collection) =>
        _vm.CollectionsPanel.Tree.Single(node => node.Collection == collection);

    private RequestTabViewModel OpenTab(RequestItem request)
    {
        _vm.OpenRequestCommand.Execute(request);
        return _vm.ActiveTab!;
    }

    [Fact]
    public async Task DeleteCollection_RemovesItAndClosesOnlyTheTabsOfItsRequests()
    {
        var inApi = AddRequest(_api, "list-users", _users.Id);
        var inAuth = AddRequest(_auth, "login");
        var authTab = OpenTab(inAuth);
        OpenTab(inApi);
        _vm.AddTabCommand.Execute(null);
        var draftTab = _vm.ActiveTab!;
        _vm.OpenRequestCommand.Execute(inApi);
        var savesBefore = _service.SaveCallCount;

        await _vm.CollectionsPanel.DeleteCollectionCommand.ExecuteAsync(_api);

        Assert.Equal(new[] { _auth }, _vm.CollectionsPanel.Collections);
        Assert.DoesNotContain(_vm.CollectionsPanel.Tree, node => node.Collection == _api);
        Assert.Equal(new[] { authTab, draftTab }, _vm.OpenRequests);
        Assert.Same(draftTab, _vm.ActiveTab);
        Assert.Equal(savesBefore + 1, _service.SaveCallCount);
        Assert.DoesNotContain(_api, _service.LastSaved!.Collections);
    }

    [Fact]
    public async Task DeleteCollection_WithNoOpenTabs_LeavesTabsEmpty()
    {
        AddRequest(_api, "list-users");

        await _vm.CollectionsPanel.DeleteCollectionCommand.ExecuteAsync(_api);

        Assert.Empty(_vm.OpenRequests);
        Assert.Null(_vm.ActiveTab);
    }

    [Fact]
    public async Task DeleteRequest_RemovesItFromItsFolderAndClosesOnlyItsTab()
    {
        var doomed = AddRequest(_api, "list-users", _users.Id);
        var sibling = AddRequest(_api, "get-user", _users.Id);
        var siblingTab = OpenTab(sibling);
        OpenTab(doomed);
        _vm.SaveWorkspaceToDisk();
        var savesBefore = _service.SaveCallCount;

        await _vm.CollectionsPanel.DeleteRequestCommand.ExecuteAsync(doomed);

        Assert.Equal(new[] { "get-user" }, Names(_api, _users.Id));
        Assert.Equal(new[] { sibling }, TreeNode(_api).Folders.Single(f => f.Folder == _users).Requests);
        Assert.Equal(new[] { siblingTab }, _vm.OpenRequests);
        Assert.Same(siblingTab, _vm.ActiveTab);
        Assert.Equal(savesBefore + 1, _service.SaveCallCount);
    }

    [Fact]
    public async Task DeleteRequest_WhileSearching_DropsItFromSearchResults()
    {
        var doomed = AddRequest(_api, "list-users");
        AddRequest(_auth, "users-login");
        _vm.CollectionsPanel.SearchText = "users";
        Assert.Equal(2, _vm.CollectionsPanel.SearchResults.Count);

        await _vm.CollectionsPanel.DeleteRequestCommand.ExecuteAsync(doomed);

        Assert.Equal(new[] { "users-login" }, _vm.CollectionsPanel.SearchResults.Select(r => r.Name));
    }

    [Fact]
    public async Task DeleteRequest_NotInAnyCollection_DoesNothing()
    {
        AddRequest(_api, "list-users");
        var savesBefore = _service.SaveCallCount;

        await _vm.CollectionsPanel.DeleteRequestCommand.ExecuteAsync(new RequestItem { Name = "stray" });

        Assert.Single(_api.Requests);
        Assert.Equal(savesBefore, _service.SaveCallCount);
    }

    [Fact]
    public async Task DeleteFolder_PromotesDirectRequestsAndChildFoldersToRoot_AndRenumbersRootRequests()
    {
        AddRequest(_api, "root");
        var a = AddRequest(_api, "a", _users.Id);
        AddRequest(_api, "b", _users.Id);
        var nested = AddRequest(_api, "nested", _admin.Id);
        var tab = OpenTab(a);
        var savesBefore = _service.SaveCallCount;

        await _vm.CollectionsPanel.DeleteFolderCommand.ExecuteAsync(_users);

        Assert.Equal(new[] { _admin }, _api.Folders);
        Assert.Null(_admin.ParentFolderId);
        Assert.Equal(new[] { "root", "a", "b" }, Names(_api, null));
        Assert.Equal(new[] { 0, 1, 2 }, SortOrders(_api, null));
        Assert.Equal(_admin.Id, nested.FolderId);
        Assert.Equal(new[] { tab }, _vm.OpenRequests);
        Assert.Equal(savesBefore + 1, _service.SaveCallCount);

        var node = TreeNode(_api);
        Assert.Equal(new[] { _admin }, node.Folders.Select(f => f.Folder));
        Assert.Equal(new[] { nested }, node.Folders.Single().Requests);
        Assert.Equal(3, node.Requests.Count);
    }

    [Fact]
    public async Task DeleteCollection_SaveFailure_KeepsTheDeletionInMemoryAndShowsTheSaveError()
    {
        _service.SaveException = new UnauthorizedAccessException("read-only");

        await _vm.CollectionsPanel.DeleteCollectionCommand.ExecuteAsync(_api);

        Assert.DoesNotContain(_api, _vm.CollectionsPanel.Collections);
        Assert.Equal("read-only", _vm.WorkspaceSaveError);
    }

    [Fact]
    public async Task DeleteFolder_SaveFailure_KeepsTheDeletionInMemoryAndShowsTheSaveError()
    {
        _service.SaveException = new UnauthorizedAccessException("read-only");

        await _vm.CollectionsPanel.DeleteFolderCommand.ExecuteAsync(_users);

        Assert.DoesNotContain(_users, _api.Folders);
        Assert.Equal("read-only", _vm.WorkspaceSaveError);
    }

    [Fact]
    public async Task DeleteRequest_SaveFailure_KeepsTheDeletionInMemoryAndShowsTheSaveError()
    {
        var request = AddRequest(_api, "list-users", _users.Id);
        _service.SaveException = new UnauthorizedAccessException("read-only");

        await _vm.CollectionsPanel.DeleteRequestCommand.ExecuteAsync(request);

        Assert.DoesNotContain(request, _api.Requests);
        Assert.Equal("read-only", _vm.WorkspaceSaveError);
    }

    [Fact]
    public void RenameRequest_WhileSearching_RefreshesWhichRequestsMatch()
    {
        var request = AddRequest(_api, "list-users");
        request.Url = "https://api.example.test/people";
        _vm.CollectionsPanel.SearchText = "users";
        Assert.Single(_vm.CollectionsPanel.SearchResults);

        request.Name = "list-people";
        _vm.CollectionsPanel.RenameRequestCommand.Execute(request);

        Assert.Empty(_vm.CollectionsPanel.SearchResults);
    }

    [Fact]
    public async Task DuplicateRequest_InFolder_InsertsCopyRightAfterOriginalAndRenumbersSiblings()
    {
        var a = AddRequest(_api, "a", _users.Id);
        AddRequest(_api, "b", _users.Id);
        AddRequest(_api, "root");
        var savesBefore = _service.SaveCallCount;

        await _vm.CollectionsPanel.DuplicateRequestCommand.ExecuteAsync(a);

        Assert.Equal(new[] { "a", "Copy of a", "b" }, Names(_api, _users.Id));
        Assert.Equal(new[] { 0, 1, 2 }, SortOrders(_api, _users.Id));
        Assert.Equal(new[] { "root" }, Names(_api, null));
        Assert.Equal(savesBefore + 1, _service.SaveCallCount);
        Assert.Equal(new[] { "a", "Copy of a", "b" }, TreeNode(_api).Folders.Single(f => f.Folder == _users).Requests.Select(r => r.Name));
    }

    [Fact]
    public void RenameRequest_OpenCleanTab_UpdatesItsTitleWithoutMarkingItDirty()
    {
        var request = AddRequest(_api, "list-users");
        var tab = OpenTab(request);

        request.Name = "  list-people  ";
        _vm.CollectionsPanel.RenameRequestCommand.Execute(request);

        Assert.Equal("list-people", tab.Name);
        Assert.False(tab.IsDirty);
    }

    [Fact]
    public void RenameRequest_OpenDirtyTab_UpdatesItsTitleAndKeepsItsUnsavedEdits()
    {
        var request = AddRequest(_api, "list-users");
        var tab = OpenTab(request);
        tab.RequestUrl = "https://api.example.test/people";

        request.Name = "list-people";
        _vm.CollectionsPanel.RenameRequestCommand.Execute(request);

        Assert.Equal("list-people", tab.Name);
        Assert.True(tab.IsDirty);
        Assert.Equal("https://api.example.test/people", tab.RequestUrl);
    }

    [Fact]
    public async Task RenameRequest_ThenSavingItsOpenTab_KeepsTheNewName()
    {
        var request = AddRequest(_api, "list-users");
        var tab = OpenTab(request);
        tab.RequestUrl = "https://api.example.test/people";

        request.Name = "list-people";
        _vm.CollectionsPanel.RenameRequestCommand.Execute(request);
        await _vm.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(_api.Requests);
        Assert.Equal("list-people", saved.Name);
        Assert.Equal("https://api.example.test/people", saved.Url);
    }

    [Fact]
    public void AddRequest_OpensItInANewActiveTab()
    {
        _vm.CollectionsPanel.AddRequestCommand.Execute(_auth);

        var request = Assert.Single(_auth.Requests);
        var tab = Assert.Single(_vm.OpenRequests);
        Assert.Same(tab, _vm.ActiveTab);
        Assert.Equal(request.Id, tab.RequestId);
    }

    [Fact]
    public void OpenRequest_AlreadyOpen_ActivatesTheExistingTab()
    {
        var a = AddRequest(_api, "a");
        var b = AddRequest(_api, "b");
        var tabA = OpenTab(a);
        OpenTab(b);

        _vm.OpenRequestCommand.Execute(a);

        Assert.Equal(2, _vm.OpenRequests.Count);
        Assert.Same(tabA, _vm.ActiveTab);
    }
}
