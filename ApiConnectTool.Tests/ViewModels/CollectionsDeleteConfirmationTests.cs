using System.Collections.ObjectModel;
using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class CollectionsDeleteConfirmationTests
{
    [Fact]
    public void DeleteCollection_WithoutHandler_PreservesCollection()
    {
        var collection = new RequestCollection { Name = "API" };
        var vm = new CollectionsViewModel(new ObservableCollection<RequestCollection> { collection });

        vm.DeleteCollectionCommand.Execute(collection);

        Assert.Contains(collection, vm.Collections);
    }
    [Theory]
    [InlineData(CollectionItemKind.Collection, true)]
    [InlineData(CollectionItemKind.Collection, false)]
    [InlineData(CollectionItemKind.Collection, null)]
    [InlineData(CollectionItemKind.Folder, true)]
    [InlineData(CollectionItemKind.Folder, false)]
    [InlineData(CollectionItemKind.Folder, null)]
    [InlineData(CollectionItemKind.Request, true)]
    [InlineData(CollectionItemKind.Request, false)]
    [InlineData(CollectionItemKind.Request, null)]
    public async Task Delete_WaitsForConfirmation_AndPreservesDataAndTabsUnlessAccepted(
        CollectionItemKind kind, bool? accepted)
    {
        var collection = new RequestCollection { Name = "API" };
        var folder = new CollectionFolder { Name = "Users" };
        var child = new CollectionFolder { Name = "Admin", ParentFolderId = folder.Id };
        var request = new RequestItem { Name = "List users", FolderId = folder.Id };
        var nested = new RequestItem { Name = "Audit", FolderId = child.Id };
        collection.Folders.Add(folder);
        collection.Folders.Add(child);
        collection.Requests.Add(request);
        collection.Requests.Add(nested);
        var service = new FakeWorkspaceService
        {
            WorkspaceToReturn = new Workspace
            {
                Collections = new ObservableCollection<RequestCollection> { collection }
            }
        };
        var main = new MainWindowViewModel(service);
        var vm = main.CollectionsPanel;
        main.OpenRequestCommand.Execute(request);
        main.OpenRequestCommand.Execute(nested);
        var tabs = main.OpenRequests.ToArray();
        var activeTab = main.ActiveTab;
        var tree = vm.Tree.Single();
        vm.SearchText = "List";
        var savesBefore = service.SaveCallCount;
        var confirmation = new TaskCompletionSource<bool>();
        var prompts = new List<(CollectionItemKind Kind, string Name)>();
        if (accepted.HasValue)
        {
            vm.DeleteConfirmationHandler = (itemKind, name) =>
            {
                prompts.Add((itemKind, name));
                return confirmation.Task;
            };
        }

        var deletion = kind switch
        {
            CollectionItemKind.Collection => vm.DeleteCollectionCommand.ExecuteAsync(collection),
            CollectionItemKind.Folder => vm.DeleteFolderCommand.ExecuteAsync(folder),
            CollectionItemKind.Request => vm.DeleteRequestCommand.ExecuteAsync(request),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        AssertPreserved();
        if (accepted.HasValue)
        {
            Assert.False(deletion.IsCompleted);
            var expectedName = kind switch
            {
                CollectionItemKind.Collection => "API",
                CollectionItemKind.Folder => "Users",
                _ => "List users"
            };
            Assert.Equal((kind, expectedName), Assert.Single(prompts));
            confirmation.SetResult(accepted.Value);
        }
        await deletion;

        if (accepted != true)
        {
            AssertPreserved();
            return;
        }

        Assert.Equal(savesBefore + 1, service.SaveCallCount);
        if (kind == CollectionItemKind.Collection)
        {
            Assert.Empty(vm.Collections);
            Assert.Empty(vm.Tree);
            Assert.Empty(vm.SearchResults);
            Assert.Empty(main.OpenRequests);
        }
        else if (kind == CollectionItemKind.Folder)
        {
            Assert.Equal(new[] { child }, collection.Folders);
            Assert.Null(child.ParentFolderId);
            Assert.Null(request.FolderId);
            Assert.Equal(child.Id, nested.FolderId);
            Assert.Equal(tabs, main.OpenRequests);
        }
        else
        {
            Assert.Equal(new[] { nested }, collection.Requests);
            Assert.Equal(new[] { tabs[1] }, main.OpenRequests);
            Assert.Empty(vm.SearchResults);
        }

        void AssertPreserved()
        {
            Assert.Equal(new[] { collection }, vm.Collections);
            Assert.Equal(new[] { folder, child }, collection.Folders);
            Assert.Equal(new[] { request, nested }, collection.Requests);
            Assert.Equal(folder.Id, request.FolderId);
            Assert.Equal(child.Id, nested.FolderId);
            Assert.Equal(folder.Id, child.ParentFolderId);
            Assert.Equal(tabs, main.OpenRequests);
            Assert.Same(activeTab, main.ActiveTab);
            Assert.Same(tree, Assert.Single(vm.Tree));
            Assert.Equal(new[] { request }, vm.SearchResults);
            Assert.Equal(savesBefore, service.SaveCallCount);
        }
    }
}
