using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ApiConnectTool.Models;
using ApiConnectTool.Tests.ViewModels;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views;
using Xunit;

namespace ApiConnectTool.Tests.Views;

/// <summary>Headless checks of what the Move / Save location pickers actually render. Regression
/// for folders being unfindable when the picker labelled every entry with its full path, which a
/// narrow dropdown cut off right after a long collection name.</summary>
public class LocationPickerHeadlessTests
{
    private const string LongCollectionName = "Sample APIs for Ordering, Invoicing and Master Data import";

    private static (MainWindowViewModel Vm, RequestCollection Collection, RequestItem Request) CreateWorkspace()
    {
        var collection = new RequestCollection { Name = LongCollectionName };
        var testFolder = new CollectionFolder { Name = "Test Folder" };
        collection.Folders.Add(testFolder);
        collection.Folders.Add(new CollectionFolder { Name = "Nested", ParentFolderId = testFolder.Id });
        var request = new RequestItem { Name = "Returns invoices approved for transfer to accounting." };
        collection.Requests.Add(request);
        var other = new RequestCollection { Name = "My API" };
        var service = new FakeWorkspaceService
        {
            WorkspaceToReturn = new Workspace { Collections = new ObservableCollection<RequestCollection> { other, collection } }
        };
        return (new MainWindowViewModel(service), collection, request);
    }

    private static string[] VisibleLabels(Window window) =>
        window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && text.FindAncestorOfType<ListBoxItem>() is not null)
            .Select(text => text.Text ?? string.Empty)
            .ToArray();

    [Fact]
    public Task MoveDialog_ListsEveryFolderByItsOwnName() => HeadlessSession.RunAsync(() =>
    {
        var (vm, _, request) = CreateWorkspace();
        var destinations = vm.CollectionsPanel.GetSaveDestinations();
        var window = MoveRequestWindow.Create(request, destinations, destinations.First(d => d.DisplayPath == LongCollectionName));
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { "My API", LongCollectionName, "Test Folder", "Nested" }, VisibleLabels(window));
        window.Close();
    });

    [Fact]
    public Task MoveDialog_FoldersAreIndentedUnderTheirCollection() => HeadlessSession.RunAsync(() =>
    {
        var (vm, _, request) = CreateWorkspace();
        var destinations = vm.CollectionsPanel.GetSaveDestinations();
        var window = MoveRequestWindow.Create(request, destinations, null);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        double LeftOf(string label) => window.GetVisualDescendants().OfType<TextBlock>()
            .Single(text => text.IsEffectivelyVisible && text.Text == label && text.FindAncestorOfType<ListBoxItem>() is not null)
            .TranslatePoint(new Point(0, 0), window)!.Value.X;

        Assert.True(LeftOf("Test Folder") > LeftOf(LongCollectionName));
        Assert.True(LeftOf("Nested") > LeftOf("Test Folder"));
        window.Close();
    });

    [Fact]
    public Task MoveDialog_PickingAFolderAndClickingMove_ReturnsThatFolder() => HeadlessSession.RunAsync(async () =>
    {
        var (vm, collection, request) = CreateWorkspace();
        var destinations = vm.CollectionsPanel.GetSaveDestinations();
        var testFolder = destinations.Single(d => d.Label == "Test Folder");
        var owner = new Window();
        owner.Show();

        var result = MoveRequestWindow.ShowAsync(owner, request, destinations, destinations.First(d => d.Collection == collection && d.IsCollection));
        Dispatcher.UIThread.RunJobs();
        var dialog = Assert.IsType<MoveRequestWindow>(Assert.Single(owner.OwnedWindows));
        dialog.LocationList.SelectedItem = testFolder;
        dialog.MoveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Same(testFolder, await result);
        owner.Close();
    });

    [Fact]
    public Task MoveDialog_EndToEnd_RequestEndsUpInTheFolder() => HeadlessSession.RunAsync(async () =>
    {
        var (vm, collection, request) = CreateWorkspace();
        var owner = new Window();
        owner.Show();
        vm.CollectionsPanel.MoveDestinationPromptHandler = (r, destinations, current) =>
        {
            var shown = MoveRequestWindow.ShowAsync(owner, r, destinations, current);
            Dispatcher.UIThread.RunJobs();
            var dialog = (MoveRequestWindow)owner.OwnedWindows.Single();
            dialog.LocationList.SelectedItem = destinations.Single(d => d.Label == "Test Folder");
            dialog.MoveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return shown;
        };

        await vm.CollectionsPanel.MoveRequestToCommand.ExecuteAsync(request);

        Assert.Equal(collection.Folders.Single(f => f.Name == "Test Folder").Id, request.FolderId);
        owner.Close();
    });

    [Fact]
    public Task SaveDialog_FirstSave_ListsEveryFolderByItsOwnName() => HeadlessSession.RunAsync(() =>
    {
        var (vm, _, _) = CreateWorkspace();
        var destinations = vm.CollectionsPanel.GetSaveDestinations();
        var window = SaveRequestAsWindow.Create(new SaveAsPromptRequest(SaveAsPromptMode.SaveNew, "Draft", destinations, destinations[0]));
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { "My API", LongCollectionName, "Test Folder", "Nested" }, VisibleLabels(window));
        window.Close();
    });

    [Fact]
    public Task SaveDialog_Duplicate_HidesTheLocationList() => HeadlessSession.RunAsync(() =>
    {
        var (vm, _, _) = CreateWorkspace();
        var window = SaveRequestAsWindow.Create(new SaveAsPromptRequest(SaveAsPromptMode.Duplicate, "Copy", [], null));
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.LocationList.IsEffectivelyVisible);
        window.Close();
    });
}
