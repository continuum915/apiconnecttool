using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

/// <summary>Tree projection and sidebar search, driven directly against the collections view
/// model without constructing tabs, history, or environments.</summary>
public class CollectionsViewModelTests
{
    private readonly RequestCollection _api = new() { Name = "My API" };
    private readonly RequestCollection _auth = new() { Name = "Auth" };
    private readonly CollectionFolder _users = new() { Name = "Users", SortOrder = 0 };
    private readonly CollectionFolder _admin;
    private readonly CollectionsViewModel _vm;

    public CollectionsViewModelTests()
    {
        _admin = new CollectionFolder { Name = "Admin", ParentFolderId = _users.Id };
        _api.Folders.Add(_users);
        _api.Folders.Add(_admin);
        _vm = new CollectionsViewModel(new ObservableCollection<RequestCollection> { _api, _auth });
    }

    private static RequestItem AddRequest(RequestCollection collection, string name, Guid? folderId = null, HttpMethod? method = null)
    {
        var request = new RequestItem
        {
            Name = name,
            Url = $"https://api.example.test/{name}",
            Method = method ?? HttpMethod.Get,
            FolderId = folderId,
            SortOrder = collection.Requests.Count(r => r.FolderId == folderId)
        };
        collection.Requests.Add(request);
        return request;
    }

    private CollectionTreeNode TreeNode(RequestCollection collection) =>
        _vm.Tree.Single(node => node.Collection == collection);

    [Fact]
    public void Collections_IsTheListPassedIn()
    {
        var collections = new ObservableCollection<RequestCollection> { _api };
        var vm = new CollectionsViewModel(collections);

        Assert.Same(collections, vm.Collections);
    }

    [Fact]
    public void Tree_ProjectsCollectionsNestedFoldersAndRequestsInSortOrder()
    {
        var second = AddRequest(_api, "second", _users.Id);
        var first = AddRequest(_api, "first", _users.Id);
        first.SortOrder = 0;
        second.SortOrder = 1;
        var nested = AddRequest(_api, "nested", _admin.Id);
        var root = AddRequest(_api, "root");

        _vm.Refresh();

        Assert.Equal(new[] { _api, _auth }, _vm.Tree.Select(node => node.Collection));
        var apiNode = TreeNode(_api);
        Assert.Equal(new[] { root }, apiNode.Requests);
        var usersNode = Assert.Single(apiNode.Folders);
        Assert.Same(_users, usersNode.Folder);
        Assert.Equal(new[] { first, second }, usersNode.Requests);
        var adminNode = Assert.Single(usersNode.Folders);
        Assert.Same(_admin, adminNode.Folder);
        Assert.Equal(new[] { nested }, adminNode.Requests);
        Assert.Empty(TreeNode(_auth).Folders);
    }

    [Fact]
    public void Tree_IsBuiltOnConstruction()
    {
        AddRequest(_auth, "login");

        var vm = new CollectionsViewModel(new ObservableCollection<RequestCollection> { _auth });

        Assert.Equal("login", Assert.Single(Assert.Single(vm.Tree).Requests).Name);
    }

    [Fact]
    public void Refresh_UnrelatedEdit_PreservesNodeIdentity()
    {
        AddRequest(_api, "list-users", _users.Id);
        _vm.Refresh();
        var apiNode = TreeNode(_api);
        var usersNode = apiNode.Folders.Single();
        var adminNode = usersNode.Folders.Single();

        AddRequest(_auth, "login");
        _auth.Name = "Renamed";
        _vm.Refresh();

        Assert.Same(apiNode, TreeNode(_api));
        Assert.Same(usersNode, TreeNode(_api).Folders.Single());
        Assert.Same(adminNode, TreeNode(_api).Folders.Single().Folders.Single());
    }

    [Fact]
    public void Refresh_ReplacedRequestInstance_ShowsTheNewInstance()
    {
        var original = AddRequest(_api, "list-users", _users.Id);
        _vm.Refresh();
        var usersNode = TreeNode(_api).Folders.Single();

        var saved = original.Clone();
        saved.Id = original.Id;
        saved.Url = "https://api.example.test/v2/users";
        _api.Requests[_api.Requests.IndexOf(original)] = saved;
        _vm.Refresh();

        Assert.Same(usersNode, TreeNode(_api).Folders.Single());
        Assert.Same(saved, Assert.Single(usersNode.Requests));
    }

    [Fact]
    public void Refresh_RemovedCollectionAndFolder_DropTheirNodes()
    {
        var inAdmin = AddRequest(_api, "audit", _admin.Id);
        _vm.Refresh();

        _vm.Collections.Remove(_auth);
        _api.Folders.Remove(_admin);
        inAdmin.FolderId = null;
        _vm.Refresh();

        Assert.Equal(new[] { _api }, _vm.Tree.Select(node => node.Collection));
        Assert.Empty(TreeNode(_api).Folders.Single().Folders);
        Assert.Equal(new[] { inAdmin }, TreeNode(_api).Requests);
    }

    [Fact]
    public void SearchText_MatchesNestedRequestsByNameUrlOrMethodAcrossCollections()
    {
        AddRequest(_api, "list-users", _users.Id);
        AddRequest(_api, "audit-log", _admin.Id);
        AddRequest(_auth, "login", method: HttpMethod.Post);
        _vm.Refresh();

        _vm.SearchText = "audit";
        Assert.True(_vm.IsSearchActive);
        Assert.Equal(new[] { "audit-log" }, _vm.SearchResults.Select(r => r.Name));

        _vm.SearchText = "example.test/list";
        Assert.Equal(new[] { "list-users" }, _vm.SearchResults.Select(r => r.Name));

        _vm.SearchText = "  post ";
        Assert.Equal(new[] { "login" }, _vm.SearchResults.Select(r => r.Name));
    }

    [Fact]
    public void SearchText_Blank_ClearsResultsAndDeactivatesSearch()
    {
        AddRequest(_api, "list-users", _users.Id);
        _vm.SearchText = "users";
        Assert.Single(_vm.SearchResults);

        _vm.SearchText = "   ";

        Assert.False(_vm.IsSearchActive);
        Assert.Empty(_vm.SearchResults);
    }

    [Fact]
    public void SearchText_RaisesIsSearchActiveChanged()
    {
        var changed = new List<string?>();
        _vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _vm.SearchText = "users";

        Assert.Contains(nameof(CollectionsViewModel.IsSearchActive), changed);
    }

    [Fact]
    public void Refresh_AfterRename_UpdatesSearchResults()
    {
        var request = AddRequest(_api, "list-users", _users.Id);
        _vm.SearchText = "users";
        Assert.Single(_vm.SearchResults);

        request.Name = "list-members";
        request.Url = "https://api.example.test/members";
        _vm.Refresh();

        Assert.Empty(_vm.SearchResults);
    }

    [Fact]
    public void Refresh_AfterSaveReplacesInstance_SearchResultsShowSavedVersion()
    {
        var original = AddRequest(_api, "list-users", _admin.Id);
        _vm.SearchText = "users";

        var saved = original.Clone();
        saved.Id = original.Id;
        saved.Url = "https://api.example.test/users/2";
        _api.Requests[_api.Requests.IndexOf(original)] = saved;
        _vm.Refresh();

        Assert.Same(saved, Assert.Single(_vm.SearchResults));
    }
}
