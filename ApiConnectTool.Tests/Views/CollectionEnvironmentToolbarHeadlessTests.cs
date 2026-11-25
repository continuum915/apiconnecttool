using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ApiConnectTool.Models;
using ApiConnectTool.Tests.ViewModels;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views.Components;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class CollectionEnvironmentToolbarHeadlessTests
{
    private static (MainWindowViewModel Vm, RequestCollection Orders, RequestItem Request, RequestEnvironment Prod) Arrange(bool withGlobal)
    {
        var dev = new RequestEnvironment { Name = "dev" };
        var prod = new RequestEnvironment { Name = "prod", IsProduction = true };
        var request = new RequestItem { Name = "List orders", Url = "https://orders.test" };
        var orders = new RequestCollection { Name = "Orders", Environments = { dev, prod }, ActiveEnvironmentId = dev.Id, Requests = { request } };
        var workspace = new Workspace { Collections = new ObservableCollection<RequestCollection> { orders } };
        if (withGlobal) workspace.Environments = new ObservableCollection<RequestEnvironment> { new() { Name = "shared" } };
        return (new MainWindowViewModel(new FakeWorkspaceService { WorkspaceToReturn = workspace }), orders, request, prod);
    }

    private static Window Show(Control content, object dataContext)
    {
        var window = new Window { Width = 1200, Height = 600, DataContext = dataContext, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return window;
    }

    [Fact]
    public Task Toolbar_HidesTheGlobalPickerUntilAGlobalEnvironmentExists() => HeadlessSession.RunAsync(() =>
    {
        var (vm, _, _, _) = Arrange(withGlobal: false);
        var toolbar = new AppToolbarControl();
        var window = Show(toolbar, vm);
        try
        {
            Assert.False(toolbar.FindControl<StackPanel>("GlobalEnvironmentPanel")!.IsVisible);
            Assert.True(toolbar.FindControl<ComboBox>("CollectionEnvironmentPicker")!.IsVisible);

            vm.Environments.Add(new RequestEnvironment { Name = "shared" });
            Dispatcher.UIThread.RunJobs();

            Assert.True(toolbar.FindControl<StackPanel>("GlobalEnvironmentPanel")!.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Toolbar_CollectionPickerIsDisabledForDraftTabsAndEnabledForCollectionTabs() => HeadlessSession.RunAsync(() =>
    {
        var (vm, orders, request, _) = Arrange(withGlobal: true);
        var toolbar = new AppToolbarControl();
        var window = Show(toolbar, vm);
        try
        {
            var picker = toolbar.FindControl<ComboBox>("CollectionEnvironmentPicker")!;
            Assert.True(toolbar.FindControl<StackPanel>("GlobalEnvironmentPanel")!.IsVisible);

            vm.AddTabCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(picker.IsEnabled);
            Assert.Equal(vm.CollectionEnvironmentPicker.ToolTip, ToolTip.GetTip(picker));

            vm.OpenRequestCommand.Execute(request);
            Dispatcher.UIThread.RunJobs();
            Assert.True(picker.IsEnabled);
            Assert.True(picker.Focusable);
            Assert.Equal(5, picker.ItemCount); // three choices, then the two actions
            Assert.Same(orders.ActiveEnvironment, ((EnvironmentChoice)picker.SelectedItem!).Environment);

            picker.SelectedIndex = 2;
            Assert.Equal(orders.Environments[1].Id, orders.ActiveEnvironmentId);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Sidebar_ShowsTheActiveEnvironmentAndMarksProduction() => HeadlessSession.RunAsync(() =>
    {
        var (vm, orders, _, prod) = Arrange(withGlobal: false);
        var sidebar = new SidebarControl();
        var window = Show(sidebar, vm);
        try
        {
            TextBlock Label() => sidebar.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("collection-environment"));

            Assert.Equal("dev", Label().Text);
            Assert.True(Label().IsVisible);
            Assert.DoesNotContain("production", Label().Classes);

            orders.ActiveEnvironmentId = prod.Id;
            vm.SaveWorkspaceToDisk();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("prod", Label().Text);
            Assert.Contains("production", Label().Classes);
            Assert.True(sidebar.TryFindResource("ApiConnectTool.Brush.Danger", window.ActualThemeVariant, out var danger));
            Assert.Same(danger, Label().Foreground);
            var tag = sidebar.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("prod-tag"));
            Assert.True(tag.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    });
}
