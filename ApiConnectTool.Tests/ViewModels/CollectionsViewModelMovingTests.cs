using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

/// <summary>Destination listing and moving/reordering through the collections panel.</summary>
public class CollectionsViewModelMovingTests
{
    private readonly RequestCollection _api = new() { Name = "My API" };
    private readonly RequestCollection _auth = new() { Name = "Auth" };
    private readonly CollectionFolder _users = new() { Name = "Users", SortOrder = 0 };
    private readonly CollectionFolder _admin;
    private readonly CollectionFolder _orders = new() { Name = "Orders", SortOrder = 1 };
    private int _persistCount;
    private readonly CollectionsViewModel _vm;

    public CollectionsViewModelMovingTests()
    {
        _admin = new CollectionFolder { Name = "Admin", ParentFolderId = _users.Id };
        _api.Folders.Add(_orders);
        _api.Folders.Add(_users);
        _api.Folders.Add(_admin);
        _vm = new CollectionsViewModel(new ObservableCollection<RequestCollection> { _api, _auth },
            persist: () => { _persistCount++; return true; });
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

    private CollectionTreeNode TreeNode(RequestCollection collection) =>
        _vm.Tree.Single(node => node.Collection == collection);

    private static int[] SortOrders(RequestCollection collection, Guid? folderId) =>
        collection.Requests.Where(r => r.FolderId == folderId).OrderBy(r => r.SortOrder).Select(r => r.SortOrder).ToArray();

    [Fact]
    public void PlaceSavedRequest_CreatesDefaultCollectionAndCanUndoWithoutPersisting()
    {
        var collections = new ObservableCollection<RequestCollection>();
        var vm = new CollectionsViewModel(collections, persist: () => throw new InvalidOperationException("Placement must not persist"));
        var snapshot = new RequestItem();

        var undo = vm.PlaceSavedRequest(snapshot, "Draft", null);

        Assert.Equal("Requests", Assert.Single(collections).Name);
        Assert.Equal("Draft", snapshot.Name);
        Assert.Same(snapshot, Assert.Single(Assert.Single(vm.Tree).Requests));
        undo();
        Assert.Empty(collections);
        Assert.Empty(vm.Tree);
    }

    [Fact]
    public void GetSaveDestinations_ListsEachCollectionThenItsFoldersDepthFirstBySortOrder()
    {
        var paths = _vm.GetSaveDestinations().Select(destination => destination.DisplayPath);

        Assert.Equal(
            new[] { "My API", "My API / Users", "My API / Users / Admin", "My API / Orders", "Auth" },
            paths);
    }

    [Fact]
    public async Task MoveRequestTo_MovesToTheEndOfTheChosenLocation_AndClosesTheGapBehind()
    {
        var first = AddRequest(_api, "first", _users.Id);
        var moving = AddRequest(_api, "moving", _users.Id);
        var last = AddRequest(_api, "last", _users.Id);
        AddRequest(_auth, "login");
        SaveDestination? offeredCurrent = null;
        _vm.MoveDestinationPromptHandler = (request, destinations, current) =>
        {
            offeredCurrent = current;
            return Task.FromResult<SaveDestination?>(destinations.Single(d => d.DisplayPath == "Auth"));
        };

        await _vm.MoveRequestToCommand.ExecuteAsync(moving);

        Assert.Equal("My API / Users", offeredCurrent!.DisplayPath);
        Assert.Equal(new[] { "login", "moving" }, Names(_auth, null));
        Assert.Equal(new[] { "first", "last" }, Names(_api, _users.Id));
        Assert.Equal(new[] { 0, 1 }, new[] { first.SortOrder, last.SortOrder });
        Assert.Equal(1, _persistCount);
    }

    [Fact]
    public async Task MoveRequestTo_IntoNestedFolder_ShowsUnderThatFolderInTheSidebar()
    {
        var moving = AddRequest(_api, "moving");
        _vm.MoveDestinationPromptHandler = (_, destinations, _) =>
            Task.FromResult<SaveDestination?>(destinations.Single(d => d.DisplayPath == "My API / Users / Admin"));

        await _vm.MoveRequestToCommand.ExecuteAsync(moving);

        var apiNode = _vm.Tree.Single(node => node.Collection == _api);
        var adminNode = apiNode.Folders.Single(f => f.Folder == _users).Folders.Single(f => f.Folder == _admin);
        Assert.Same(moving, Assert.Single(adminNode.Requests));
        Assert.Empty(apiNode.Requests);
    }

    [Fact]
    public async Task MoveRequestTo_CancelledOrSameLocation_DoesNothing()
    {
        var request = AddRequest(_api, "stay", _users.Id);
        var savesBefore = _persistCount;

        _vm.MoveDestinationPromptHandler = (_, _, _) => Task.FromResult<SaveDestination?>(null);
        await _vm.MoveRequestToCommand.ExecuteAsync(request);
        _vm.MoveDestinationPromptHandler = (_, _, current) => Task.FromResult(current);
        await _vm.MoveRequestToCommand.ExecuteAsync(request);

        Assert.Equal(_users.Id, request.FolderId);
        Assert.Equal(savesBefore, _persistCount);
    }

    [Fact]
    public void MoveUpAndDown_ReorderWithinTheSiblingGroup_AndStopAtTheEnds()
    {
        var a = AddRequest(_api, "a", _users.Id);
        var b = AddRequest(_api, "b", _users.Id);
        var c = AddRequest(_api, "c", _users.Id);
        AddRequest(_api, "root");

        _vm.MoveRequestUpCommand.Execute(c);
        Assert.Equal(new[] { "a", "c", "b" }, Names(_api, _users.Id));

        _vm.MoveRequestDownCommand.Execute(a);
        Assert.Equal(new[] { "c", "a", "b" }, Names(_api, _users.Id));

        var savesBefore = _persistCount;
        _vm.MoveRequestUpCommand.Execute(c);
        _vm.MoveRequestDownCommand.Execute(b);
        Assert.Equal(new[] { "c", "a", "b" }, Names(_api, _users.Id));
        Assert.Equal(savesBefore, _persistCount);

        Assert.Equal(new[] { "root" }, Names(_api, null));
        var usersNode = _vm.Tree.Single(node => node.Collection == _api).Folders.Single(f => f.Folder == _users);
        Assert.Equal(new[] { "c", "a", "b" }, usersNode.Requests.Select(r => r.Name));
    }
    [Fact]
    public void MoveRequest_FromFolderToRoot_ClosesTheGapInTheFolder()
    {
        AddRequest(_api, "a", _users.Id);
        var b = AddRequest(_api, "b", _users.Id);
        AddRequest(_api, "c", _users.Id);
        AddRequest(_api, "root");

        _vm.MoveRequest(b, _api, null, targetIndex: 0);

        Assert.Equal(new[] { "a", "c" }, Names(_api, _users.Id));
        Assert.Equal(new[] { 0, 1 }, SortOrders(_api, _users.Id));
        Assert.Equal(new[] { "b", "root" }, Names(_api, null));
        Assert.Equal(new[] { 0, 1 }, SortOrders(_api, null));
        Assert.Equal(new[] { b, _api.Requests.Single(r => r.Name == "root") }, TreeNode(_api).Requests);
    }

    [Fact]
    public void MoveRequest_BetweenFoldersOfTheSameCollection_RenumbersBothFolders()
    {
        var orders = new CollectionFolder { Name = "Orders", SortOrder = 1 };
        _api.Folders.Add(orders);
        AddRequest(_api, "a", _users.Id);
        var b = AddRequest(_api, "b", _users.Id);
        AddRequest(_api, "c", _users.Id);
        AddRequest(_api, "x", orders.Id);
        AddRequest(_api, "y", orders.Id);

        _vm.MoveRequest(b, _api, orders.Id, targetIndex: 1);

        Assert.Equal(new[] { "a", "c" }, Names(_api, _users.Id));
        Assert.Equal(new[] { 0, 1 }, SortOrders(_api, _users.Id));
        Assert.Equal(new[] { "x", "b", "y" }, Names(_api, orders.Id));
        Assert.Equal(new[] { 0, 1, 2 }, SortOrders(_api, orders.Id));
        Assert.Equal(new[] { "x", "b", "y" }, TreeNode(_api).Folders.Single(f => f.Folder == orders).Requests.Select(r => r.Name));
    }

}
