using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

/// <summary>Create, rename, duplicate, and delete for collections, folders, and requests, driven
/// directly against the collections view model with recording callbacks in place of the main
/// window view model.</summary>
public class CollectionsViewModelEditingTests
{
    private readonly RequestCollection _api = new() { Name = "My API" };
    private readonly RequestCollection _auth = new() { Name = "Auth" };
    private readonly CollectionFolder _users = new() { Name = "Users", SortOrder = 0 };
    private readonly CollectionFolder _admin;
    private readonly List<RequestItem> _opened = new();
    private readonly List<Guid> _removed = new();
    private readonly List<RequestItem> _renamed = new();
    private int _persistCount;
    private bool _persistSucceeds = true;
    private Func<string, Task<string?>> _resolveDuplicateName = name => Task.FromResult<string?>(name);
    private readonly CollectionsViewModel _vm;

    public CollectionsViewModelEditingTests()
    {
        _admin = new CollectionFolder { Name = "Admin", ParentFolderId = _users.Id };
        _api.Folders.Add(_users);
        _api.Folders.Add(_admin);
        _vm = new CollectionsViewModel(
            new ObservableCollection<RequestCollection> { _api, _auth },
            persist: () =>
            {
                _persistCount++;
                return _persistSucceeds;
            },
            openRequest: _opened.Add,
            requestsRemoved: ids => _removed.AddRange(ids),
            resolveDuplicateName: name => _resolveDuplicateName(name),
            requestRenamed: _renamed.Add);
        _vm.DeleteConfirmationHandler = (_, _) => Task.FromResult(true);
    }

    private static RequestItem AddRequest(RequestCollection collection, string name, Guid? folderId = null)
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

    private CollectionTreeNode TreeNode(RequestCollection collection) =>
        _vm.Tree.Single(node => node.Collection == collection);

    [Fact]
    public void MoveRequest_RefreshesTreeAndSearchWithoutHelpFromPersistence()
    {
        var first = AddRequest(_api, "first", _users.Id);
        var moving = AddRequest(_api, "moving", _users.Id);
        var last = AddRequest(_api, "last", _users.Id);
        _vm.SearchText = "moving";

        _vm.MoveRequest(moving, _auth, null, 0);

        Assert.Equal(new[] { first, last }, TreeNode(_api).Folders.Single().Requests);
        Assert.Equal(new[] { 0, 1 }, new[] { first.SortOrder, last.SortOrder });
        Assert.Same(moving, Assert.Single(TreeNode(_auth).Requests));
        Assert.Same(moving, Assert.Single(_vm.SearchResults));
        Assert.Equal(1, _persistCount);
    }

    [Fact]
    public void AddCollection_DedupesNameAppearsInTreeAndPersists()
    {
        _vm.AddCollectionCommand.Execute(null);
        _vm.AddCollectionCommand.Execute(null);

        Assert.Equal(new[] { "My API", "Auth", "New Collection", "New Collection 1" }, _vm.Collections.Select(c => c.Name));
        Assert.Equal(_vm.Collections, _vm.Tree.Select(node => node.Collection));
        Assert.Equal(2, _persistCount);
    }

    [Fact]
    public void AddRequest_AppendsAtRootShowsInTreeOpensItAndPersists()
    {
        AddRequest(_auth, "login");

        _vm.AddRequestCommand.Execute(_auth);

        Assert.Equal(new[] { "login", "New Request" }, Names(_auth, null));
        var added = _auth.Requests.Last();
        Assert.Equal(1, added.SortOrder);
        Assert.Equal(new[] { added }, _opened);
        Assert.Contains(added, TreeNode(_auth).Requests);
        Assert.Equal(1, _persistCount);
    }

    [Fact]
    public void AddFolder_AddsTopLevelFolderWithDedupedNameAndPersists()
    {
        _vm.AddFolderCommand.Execute(_api);
        _vm.AddFolderCommand.Execute(_api);

        var added = _api.Folders.Skip(2).ToList();
        Assert.Equal(new[] { "New Folder", "New Folder 1" }, added.Select(f => f.Name));
        Assert.All(added, folder => Assert.Null(folder.ParentFolderId));
        Assert.Equal(new[] { 1, 2 }, added.Select(f => f.SortOrder));
        Assert.Equal(3, TreeNode(_api).Folders.Count);
        Assert.Equal(2, _persistCount);
    }

    [Fact]
    public void RenameCollection_TrimsFallsBackWhenBlankAndPersists()
    {
        _api.Name = "  Payments  ";
        _vm.RenameCollectionCommand.Execute(_api);
        Assert.Equal("Payments", _api.Name);

        _api.Name = "   ";
        _vm.RenameCollectionCommand.Execute(_api);
        Assert.Equal("Untitled Collection", _api.Name);
        Assert.Equal(2, _persistCount);
    }

    [Fact]
    public void RenameFolder_TrimsFallsBackWhenBlankAndPersists()
    {
        _users.Name = "  People ";
        _vm.RenameFolderCommand.Execute(_users);
        Assert.Equal("People", _users.Name);

        _users.Name = "";
        _vm.RenameFolderCommand.Execute(_users);
        Assert.Equal("Untitled Folder", _users.Name);
        Assert.Equal(2, _persistCount);
    }

    [Fact]
    public void RenameRequest_TrimsReportsTheRenameRefreshesSearchAndPersists()
    {
        var request = AddRequest(_api, "list-users", _users.Id);
        request.Url = "https://api.example.test/people";
        _vm.SearchText = "users";
        Assert.Single(_vm.SearchResults);

        request.Name = "  list-people  ";
        _vm.RenameRequestCommand.Execute(request);

        Assert.Equal("list-people", request.Name);
        Assert.Equal(new[] { request }, _renamed);
        Assert.Empty(_vm.SearchResults);
        Assert.Equal(1, _persistCount);
    }

    [Fact]
    public void RenameRequest_Blank_FallsBackToDefault()
    {
        var request = AddRequest(_api, "list-users");

        request.Name = " ";
        _vm.RenameRequestCommand.Execute(request);

        Assert.Equal("Untitled Request", request.Name);
    }

    [Fact]
    public void CancelRename_RevertsNameClearsEditingAndDoesNotPersist()
    {
        _users.RenameOriginalName = "Users";
        _users.IsEditing = true;
        _users.Name = "Peop";

        _vm.CancelRenameCommand.Execute(_users);

        Assert.Equal("Users", _users.Name);
        Assert.False(_users.IsEditing);
        Assert.Equal(0, _persistCount);
    }

    [Fact]
    public async Task DuplicateRequest_UsesTheNamingCallbackInsertsAfterOriginalAndPersists()
    {
        var a = AddRequest(_api, "a", _users.Id);
        a.Assertions.Add(new() { ExpectedValue = "204" });
        AddRequest(_api, "b", _users.Id);
        string? proposed = null;
        _resolveDuplicateName = name =>
        {
            proposed = name;
            return Task.FromResult<string?>("a again");
        };

        await _vm.DuplicateRequestCommand.ExecuteAsync(a);

        Assert.Equal("Copy of a", proposed);
        Assert.Equal(new[] { "a", "a again", "b" }, Names(_api, _users.Id));
        var copy = _api.Requests.Single(r => r.Name == "a again");
        Assert.NotEqual(a.Id, copy.Id);
        Assert.Equal(a.Url, copy.Url);
        Assert.Equal("204", Assert.Single(copy.Assertions).ExpectedValue);
        Assert.NotEqual(a.Assertions[0].Id, copy.Assertions[0].Id);
        copy.Assertions[0].ExpectedValue = "500";
        Assert.Equal("204", a.Assertions[0].ExpectedValue);
        Assert.Equal(new[] { "a", "a again", "b" }, TreeNode(_api).Folders.Single().Requests.Select(r => r.Name));
        Assert.Equal(1, _persistCount);
    }

    [Fact]
    public async Task DuplicateRequest_NamingCancelled_ChangesNothing()
    {
        var a = AddRequest(_api, "a");
        _resolveDuplicateName = _ => Task.FromResult<string?>(null);

        await _vm.DuplicateRequestCommand.ExecuteAsync(a);

        Assert.Equal(new[] { a }, _api.Requests);
        Assert.Equal(0, _persistCount);
    }

    [Fact]
    public void DuplicateCollection_DeepCopiesWithFreshIdsAndRemappedFolders()
    {
        var nested = AddRequest(_api, "audit", _admin.Id);
        nested.Assertions.Add(new());

        _vm.DuplicateCollectionCommand.Execute(_api);

        var copy = _vm.Collections.Last();
        Assert.Equal("Copy of My API", copy.Name);
        Assert.Equal(new[] { "Users", "Admin" }, copy.Folders.Select(f => f.Name));
        Assert.DoesNotContain(copy.Folders, f => f.Id == _users.Id || f.Id == _admin.Id);
        var copiedAdmin = copy.Folders.Single(f => f.Name == "Admin");
        Assert.Equal(copy.Folders.Single(f => f.Name == "Users").Id, copiedAdmin.ParentFolderId);
        var copiedRequest = Assert.Single(copy.Requests);
        Assert.NotEqual(nested.Id, copiedRequest.Id);
        Assert.NotEqual(nested.Assertions[0].Id, Assert.Single(copiedRequest.Assertions).Id);
        Assert.Equal(copiedAdmin.Id, copiedRequest.FolderId);
        Assert.Contains(_vm.Tree, node => node.Collection == copy);
        Assert.Equal(1, _persistCount);
    }

    [Fact]
    public void DuplicateCollection_CopiesEnvironmentsWithFreshIdsAndMapsTheActiveOne()
    {
        var dev = new RequestEnvironment { Name = "dev", Variables = { new() { Key = "baseUrl", InitialValue = "https://dev.example.test" } } };
        var prod = new RequestEnvironment { Name = "prod", IsProduction = true, Variables = { new() { Key = "token", InitialValue = "p", IsSecret = true } } };
        _api.Environments.Add(dev);
        _api.Environments.Add(prod);
        _api.ActiveEnvironmentId = prod.Id;

        _vm.DuplicateCollectionCommand.Execute(_api);

        var copy = _vm.Collections.Last();
        Assert.Equal(new[] { "dev", "prod" }, copy.Environments.Select(environment => environment.Name));
        Assert.DoesNotContain(copy.Environments, environment => environment.Id == dev.Id || environment.Id == prod.Id);
        var copiedProd = copy.Environments[1];
        Assert.True(copiedProd.IsProduction);
        Assert.Equal(copiedProd.Id, copy.ActiveEnvironmentId);
        Assert.Equal(prod.Id, _api.ActiveEnvironmentId);
        var copiedToken = Assert.Single(copiedProd.Variables);
        Assert.NotSame(prod.Variables[0], copiedToken);
        Assert.NotEqual(prod.Variables[0].Id, copiedToken.Id);
        Assert.Equal("p", copiedToken.InitialValue.Value);
        Assert.True(copiedToken.IsSecret);

        copiedToken.InitialValueText = "changed";
        Assert.Equal("p", prod.Variables[0].InitialValue.Value);
    }

    [Fact]
    public void DuplicateCollection_WithNoActiveEnvironment_LeavesTheCopyWithoutOne()
    {
        _api.Environments.Add(new RequestEnvironment { Name = "dev" });

        _vm.DuplicateCollectionCommand.Execute(_api);

        Assert.Null(_vm.Collections.Last().ActiveEnvironmentId);
    }

    [Fact]
    public async Task DeleteCollection_ReportsItsRequestsRemovedDropsItFromTreeAndPersists()
    {
        var a = AddRequest(_api, "a", _users.Id);
        var b = AddRequest(_api, "b");
        AddRequest(_auth, "login");

        await _vm.DeleteCollectionCommand.ExecuteAsync(_api);

        Assert.Equal(new[] { _auth }, _vm.Collections);
        Assert.Equal(new[] { _auth }, _vm.Tree.Select(node => node.Collection));
        Assert.Equal(new[] { a.Id, b.Id }, _removed);
        Assert.Equal(1, _persistCount);
    }

    [Fact]
    public async Task DeleteRequest_ReportsItRemovedDropsItFromTreeAndSearchAndPersists()
    {
        var doomed = AddRequest(_api, "list-users", _users.Id);
        var sibling = AddRequest(_api, "get-users", _users.Id);
        _vm.SearchText = "users";

        await _vm.DeleteRequestCommand.ExecuteAsync(doomed);

        Assert.Equal(new[] { sibling }, _api.Requests);
        Assert.Equal(new[] { sibling }, TreeNode(_api).Folders.Single().Requests);
        Assert.Equal(new[] { sibling }, _vm.SearchResults);
        Assert.Equal(new[] { doomed.Id }, _removed);
        Assert.Equal(1, _persistCount);
    }

    [Fact]
    public async Task DeleteRequest_NotInAnyCollection_DoesNothing()
    {
        await _vm.DeleteRequestCommand.ExecuteAsync(new RequestItem { Name = "stray" });

        Assert.Empty(_removed);
        Assert.Equal(0, _persistCount);
    }

    [Fact]
    public async Task DeleteFolder_MovesDirectRequestsAndChildFoldersToRootWithoutRemovingRequests()
    {
        AddRequest(_api, "root");
        AddRequest(_api, "a", _users.Id);
        var nested = AddRequest(_api, "nested", _admin.Id);

        await _vm.DeleteFolderCommand.ExecuteAsync(_users);

        Assert.Equal(new[] { _admin }, _api.Folders);
        Assert.Null(_admin.ParentFolderId);
        Assert.Equal(new[] { "root", "a" }, Names(_api, null));
        Assert.Equal(_admin.Id, nested.FolderId);
        Assert.Equal(new[] { _admin }, TreeNode(_api).Folders.Select(f => f.Folder));
        Assert.Empty(_removed);
        Assert.Equal(1, _persistCount);
    }

    [Fact]
    public void MutatingCommand_PersistFails_KeepsTheChangeInMemoryAndStillRefreshesTheTree()
    {
        _persistSucceeds = false;

        _vm.AddCollectionCommand.Execute(null);

        Assert.Equal(3, _vm.Collections.Count);
        Assert.Equal(3, _vm.Tree.Count);
        Assert.Equal(1, _persistCount);
    }
}
