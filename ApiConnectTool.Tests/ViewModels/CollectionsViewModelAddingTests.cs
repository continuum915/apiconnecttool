using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

/// <summary>Adding requests, folders and collections from the navigator: where they land,
/// whether they start in rename mode, and the tree path handed to the view to reveal them.</summary>
public class CollectionsViewModelAddingTests
{
    private readonly RequestCollection _api = new() { Name = "My API" };
    private readonly CollectionFolder _users = new() { Name = "Users" };
    private readonly List<RequestItem> _opened = new();
    private readonly List<IReadOnlyList<object>> _revealed = new();
    private readonly CollectionsViewModel _vm;

    public CollectionsViewModelAddingTests()
    {
        _api.Folders.Add(_users);
        _api.Requests.Add(new RequestItem { Name = "List users", FolderId = _users.Id, SortOrder = 0 });
        _vm = new CollectionsViewModel(new ObservableCollection<RequestCollection> { _api }, openRequest: _opened.Add);
        _vm.NodeAdded += _revealed.Add;
    }

    [Fact]
    public void AddRequestToFolder_PutsTheRequestInsideTheFolderAfterItsSiblingsAndOpensIt()
    {
        _vm.AddRequestToFolderCommand.Execute(_users);

        var added = _api.Requests.Single(request => request.Name == "New Request");
        Assert.Equal(_users.Id, added.FolderId);
        Assert.Equal(1, added.SortOrder);
        Assert.Same(added, Assert.Single(_opened));
        var folderNode = _vm.Tree.Single().Folders.Single();
        Assert.Contains(added, folderNode.Requests);
        Assert.Equal(new object[] { _vm.Tree.Single(), folderNode, added }, Assert.Single(_revealed));
    }

    [Fact]
    public void AddFolder_StartsRenamingTheNewFolderAndRevealsIt()
    {
        _vm.AddFolderCommand.Execute(_api);

        var folder = _api.Folders.Single(candidate => candidate.Name == "New Folder");
        Assert.True(folder.IsEditing);
        Assert.Equal("New Folder", folder.RenameOriginalName);
        var path = Assert.Single(_revealed);
        Assert.Same(folder, Assert.IsType<FolderTreeNode>(path[^1]).Folder);
    }

    [Fact]
    public void AddCollection_StartsRenamingTheNewCollectionAndRevealsIt()
    {
        _vm.AddCollectionCommand.Execute(null);

        var collection = _vm.Collections.Single(candidate => candidate.Name == "New Collection");
        Assert.True(collection.IsEditing);
        Assert.Equal("New Collection", collection.RenameOriginalName);
        Assert.Same(collection, Assert.IsType<CollectionTreeNode>(Assert.Single(Assert.Single(_revealed))).Collection);
    }

    [Fact]
    public void CancellingTheRenameOfANewFolderKeepsItsDefaultName()
    {
        _vm.AddFolderCommand.Execute(_api);
        var folder = _api.Folders.Single(candidate => candidate.Name == "New Folder");
        folder.Name = "Half typed";

        _vm.CancelRenameCommand.Execute(folder);

        Assert.Equal("New Folder", folder.Name);
        Assert.False(folder.IsEditing);
    }
}
