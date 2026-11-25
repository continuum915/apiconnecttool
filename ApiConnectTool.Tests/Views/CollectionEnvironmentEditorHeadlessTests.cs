using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using ApiConnectTool.Models;
using ApiConnectTool.Tests.ViewModels;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class CollectionEnvironmentEditorHeadlessTests
{
    private static (MainWindowViewModel Vm, RequestCollection Orders) Arrange()
    {
        var orders = new RequestCollection
        {
            Name = "Orders",
            Environments = { new RequestEnvironment { Name = "orders dev" }, new RequestEnvironment { Name = "orders qa" } }
        };
        var billing = new RequestCollection { Name = "Billing", Environments = { new RequestEnvironment { Name = "billing dev" } } };
        var workspace = new Workspace
        {
            Collections = new ObservableCollection<RequestCollection> { orders, billing },
            Environments = new ObservableCollection<RequestEnvironment> { new() { Name = "shared" } }
        };
        return (new MainWindowViewModel(new FakeWorkspaceService { WorkspaceToReturn = workspace }), orders);
    }

    [Fact]
    public Task ScopedEditor_ShowsOnlyThatCollectionsEnvironments() => HeadlessSession.RunAsync(() =>
    {
        var (vm, orders) = Arrange();
        var window = new EnvironmentEditorWindow { DataContext = vm.CreateEnvironmentEditor(orders) };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var list = window.FindControl<ListBox>("EnvironmentList")!;

            Assert.Equal(new[] { "Collection variables", "orders dev", "orders qa" }, list.Items.Cast<RequestEnvironment>().Select(environment => environment.Name));
            Assert.Equal("Environments — Orders", window.Title);
            Assert.False(window.FindControl<Button>("CopyToCollectionButton")!.IsVisible);
            Assert.False(window.FindControl<Button>("MoveToCollectionButton")!.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task GlobalEditor_OffersCopyAndMoveToCollection() => HeadlessSession.RunAsync(() =>
    {
        var (vm, _) = Arrange();
        var editor = vm.CreateEnvironmentEditor();
        var window = new EnvironmentEditorWindow { DataContext = editor };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var list = window.FindControl<ListBox>("EnvironmentList")!;
            var copy = window.FindControl<Button>("CopyToCollectionButton")!;

            Assert.DoesNotContain(list.Items.Cast<RequestEnvironment>(), environment => environment.Name.StartsWith("orders"));
            Assert.Equal("Environments — Global environments", window.Title);
            Assert.True(copy.IsVisible);
            Assert.False(copy.IsEnabled);

            editor.SelectedEnvironment = editor.Environments[0];
            Dispatcher.UIThread.RunJobs();

            Assert.True(copy.IsEnabled);
            Assert.True(window.FindControl<Button>("MoveToCollectionButton")!.IsEnabled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task TransferDialog_ListsTheCollectionsAndNamesTheAction() => HeadlessSession.RunAsync(() =>
    {
        var (vm, orders) = Arrange();
        var editor = vm.CreateEnvironmentEditor();
        editor.SelectedEnvironment = editor.Environments[0];
        var window = new EnvironmentTransferWindow { DataContext = editor.CreateTransfer(move: true) };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var picker = window.FindControl<ComboBox>("CollectionPicker")!;
            Assert.Equal(2, picker.ItemCount);
            Assert.Same(orders, picker.SelectedItem);
            Assert.Equal("Move", window.FindControl<Button>("ConfirmButton")!.Content);
        }
        finally { window.Close(); }
    });
}
