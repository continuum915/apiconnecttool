using System.Collections.ObjectModel;
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

public class EnvironmentEditorLayoutHeadlessTests
{
    private static (MainWindowViewModel Vm, RequestCollection Orders, RequestEnvironment Dev) Arrange()
    {
        var dev = new RequestEnvironment
        {
            Name = "dev", IsProduction = true,
            Variables = new() { new() { Key = "host", InitialValue = "default", CurrentValue = "override", UseOverride = true },
                new() { Key = "plain", InitialValue = "value" } }
        };
        var orders = new RequestCollection { Name = "Orders", Environments = { dev }, ActiveEnvironmentId = dev.Id };
        var workspace = new Workspace { Collections = new ObservableCollection<RequestCollection> { orders } };
        return (new MainWindowViewModel(new FakeWorkspaceService { WorkspaceToReturn = workspace }), orders, dev);
    }

    [Fact]
    public Task ListShowsScopeVariablesThenEnvironmentsWithActiveAndProdMarkers() => HeadlessSession.RunAsync(() =>
    {
        var (vm, orders, _) = Arrange();
        var window = new EnvironmentEditorWindow { DataContext = vm.CreateEnvironmentEditor(orders) };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var list = window.FindControl<ListBox>("EnvironmentList")!;
            Assert.Equal(new[] { "Collection variables", "dev" }, list.Items.Cast<RequestEnvironment>().Select(environment => environment.Name));
            var visibleTexts = list.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToArray();
            Assert.Contains("Environments", visibleTexts);
            Assert.Contains("Active", visibleTexts);
            Assert.Contains("PROD", visibleTexts);
            Assert.Single(visibleTexts, text => text == "Environments");
            Assert.Equal("Active for Orders: dev", window.FindControl<TextBlock>("ActiveSummaryText")!.Text);
            Assert.True(window.FindControl<ComboBox>("ScopeSelector")!.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task OverridesInUseAreHighlightedAndScopeVariablesHideTheProductionBox() => HeadlessSession.RunAsync(() =>
    {
        var (vm, orders, dev) = Arrange();
        var editor = vm.CreateEnvironmentEditor(orders);
        var window = new EnvironmentEditorWindow { DataContext = editor, Width = 1200 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(dev, editor.SelectedEnvironment);
            Assert.True(window.FindControl<CheckBox>("ProductionCheckBox")!.IsEffectivelyVisible);
            var cells = window.GetVisualDescendants().OfType<Border>().Where(border => border.Name == "OverrideCell").ToArray();
            Assert.Equal(2, cells.Length);
            Assert.True(window.TryFindResource("ApiConnectTool.Brush.OverrideBackground", window.ActualThemeVariant, out var highlight));
            var used = cells.Single(cell => ((EnvironmentVariable)cell.DataContext!).Key == "host");
            var unused = cells.Single(cell => ((EnvironmentVariable)cell.DataContext!).Key == "plain");
            Assert.Same(highlight, used.Background);
            Assert.NotSame(highlight, unused.Background);
            var mutedDefault = window.GetVisualDescendants().OfType<TextBox>()
                .Single(box => box.Name == "DefaultValueTextBox" && ((EnvironmentVariable)box.DataContext!).Key == "host");
            Assert.True(window.TryFindResource("ApiConnectTool.Brush.TextMuted", window.ActualThemeVariant, out var muted));
            Assert.Same(muted, mutedDefault.Foreground);

            editor.SelectedEnvironment = editor.CollectionVariables;
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.FindControl<CheckBox>("ProductionCheckBox")!.IsEffectivelyVisible);
            Assert.True(window.FindControl<TextBlock>("ScopeVariablesNoteText")!.IsEffectivelyVisible);
            Assert.False(window.FindControl<Button>("SetActiveButton")!.IsEffectivelyEnabled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task EditMenuOpensTheGlobalAndTheContextualEditor() => HeadlessSession.RunAsync(() =>
    {
        var (vm, orders, _) = Arrange();
        var window = new MainWindow { DataContext = vm };
        try
        {
            window.Show();
            window.FindControl<MenuItem>("GlobalEnvironmentsMenuItem")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var global = Assert.Single(window.OwnedWindows.OfType<EnvironmentEditorWindow>());
            Assert.True(((EnvironmentEditorViewModel)global.DataContext!).IsGlobalScope);
            global.Close();

            vm.CollectionsPanel.SelectedNode = vm.CollectionsPanel.Tree.Single();
            window.FindControl<MenuItem>("EnvironmentsMenuItem")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var scoped = Assert.Single(window.OwnedWindows.OfType<EnvironmentEditorWindow>());
            Assert.Same(orders, ((EnvironmentEditorViewModel)scoped.DataContext!).Collection);
            scoped.Close();
        }
        finally { window.Close(); }
    });
}
