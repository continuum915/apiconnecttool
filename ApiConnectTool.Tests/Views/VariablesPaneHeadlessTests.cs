using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ApiConnectTool.Models;
using ApiConnectTool.Tests.ViewModels;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views;
using ApiConnectTool.Views.Components;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class VariablesPaneHeadlessTests
{
    [Fact]
    public Task PaneTogglesFromTheViewMenuAndTheToolbarAndListsVariables() => HeadlessSession.RunAsync(() =>
    {
        var request = new RequestItem { Name = "List", Url = "https://{{host}}/items" };
        var collection = new RequestCollection
        {
            Name = "Api", Requests = { request },
            Variables = new() { new() { Key = "host", InitialValue = "api.test" } }
        };
        var vm = new MainWindowViewModel(new FakeWorkspaceService
        {
            WorkspaceToReturn = new Workspace { Collections = new ObservableCollection<RequestCollection> { collection } }
        });
        vm.OpenRequestCommand.Execute(request);
        var window = new MainWindow { DataContext = vm, Width = 1400, Height = 800 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var pane = window.GetVisualDescendants().OfType<VariablesPaneControl>().Single();
            Assert.True(pane.IsEffectivelyVisible);
            var texts = pane.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
            Assert.Contains("host", texts);
            Assert.Contains("api.test", texts);
            Assert.Contains("Collection variables", texts);
            var bodyGrid = window.FindControl<Grid>("BodyGrid")!;
            Assert.Equal(260, bodyGrid.ColumnDefinitions[4].Width.Value);

            window.FindControl<MenuItem>("VariablesPaneMenuItem")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(vm.VariablesPane.IsVisible);
            Assert.False(pane.IsEffectivelyVisible);
            Assert.Equal(0, bodyGrid.ColumnDefinitions[4].Width.Value);

            var toggle = window.GetVisualDescendants().OfType<ToggleButton>().Single(button => button.Name == "VariablesToolbarButton");
            toggle.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.VariablesPane.IsVisible);
            Assert.True(pane.IsEffectivelyVisible);
            Assert.Equal(260, bodyGrid.ColumnDefinitions[4].Width.Value);
        }
        finally { window.Close(); }
    });
}
