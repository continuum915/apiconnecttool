using System.Collections.ObjectModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;
using ApiConnectTool.Tests.ViewModels;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views.Components;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class ClassicToolbarHeadlessTests
{
    private static (MainWindowViewModel Vm, RequestCollection Orders, RequestItem Request) Arrange()
    {
        var dev = new RequestEnvironment { Name = "dev" };
        var prod = new RequestEnvironment { Name = "prod", IsProduction = true };
        var request = new RequestItem { Name = "List orders", Url = "https://orders.test" };
        var orders = new RequestCollection { Name = "Orders", Environments = { dev, prod }, ActiveEnvironmentId = prod.Id, Requests = { request } };
        var workspace = new Workspace { Collections = new ObservableCollection<RequestCollection> { orders, new() { Name = "Billing" } } };
        return (new MainWindowViewModel(new FakeWorkspaceService { WorkspaceToReturn = workspace }), orders, request);
    }

    private static Window Show(Control content, object dataContext)
    {
        var window = new Window { Width = 1400, Height = 600, DataContext = dataContext, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return window;
    }

    [Fact]
    public Task ButtonsAppearInOrderWithIconsLabelsAndAutomationNames() => HeadlessSession.RunAsync(() =>
    {
        var (vm, _, _) = Arrange();
        var toolbar = new AppToolbarControl();
        var window = Show(toolbar, vm);
        try
        {
            var buttons = toolbar.FindControl<StackPanel>("ToolbarButtons")!.Children.OfType<Button>().ToArray();
            Assert.Equal(new[] { "Str.Toolbar.NewRequest", "Str.Toolbar.NewCollection", "Str.Toolbar.Import", "Str.Toolbar.RunCollection",
                    "Str.Toolbar.History", "Str.Toolbar.Console", "Str.Toolbar.Variables" }
                .Select(AppLocalization.Get), buttons.Select(button =>
                    button.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("toolbar-label")).Text));
            Assert.All(buttons, button =>
            {
                Assert.Single(button.GetVisualDescendants().OfType<AppIcon>());
                Assert.False(string.IsNullOrEmpty(AutomationProperties.GetName(button)));
            });
            Assert.Single(toolbar.FindControl<StackPanel>("ToolbarButtons")!.Children.OfType<Avalonia.Controls.Shapes.Rectangle>());
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task RunCollectionDisablesWithAnExplanationUntilThereIsATarget() => HeadlessSession.RunAsync(() =>
    {
        var (vm, orders, request) = Arrange();
        var toolbar = new AppToolbarControl();
        var window = Show(toolbar, vm);
        try
        {
            var run = toolbar.FindControl<Button>("RunCollectionToolbarButton")!;
            Assert.False(run.IsEffectivelyEnabled);
            Assert.Equal(AppLocalization.Get("Str.Toolbar.RunCollectionDisabledTip"), ToolTip.GetTip(run));

            vm.OpenRequestCommand.Execute(request);
            Dispatcher.UIThread.RunJobs();
            Assert.True(run.IsEffectivelyEnabled);
            Assert.Contains(orders.Name, (string)ToolTip.GetTip(run)!);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task PickerCaptionFollowsTheActiveTabAndMarksProduction() => HeadlessSession.RunAsync(() =>
    {
        var (vm, _, request) = Arrange();
        var toolbar = new AppToolbarControl();
        var window = Show(toolbar, vm);
        try
        {
            var caption = toolbar.FindControl<TextBlock>("CollectionEnvironmentCaption")!;
            vm.AddTabCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(AppLocalization.Get("Str.Toolbar.EnvironmentForDraft"), caption.Text);

            vm.OpenRequestCommand.Execute(request);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal("Environment for Orders", caption.Text);

            var picker = toolbar.FindControl<ComboBox>("CollectionEnvironmentPicker")!;
            var tag = picker.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Text == AppLocalization.Get("Str.Environment.ProdTag"));
            Assert.True(tag.IsEffectivelyVisible);
            Assert.DoesNotContain(picker.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("(production)") == true);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task DropDownActionsRunAndKeepTheSelectedEnvironment() => HeadlessSession.RunAsync(() =>
    {
        var (vm, orders, request) = Arrange();
        var opened = new List<RequestCollection>();
        vm.CollectionEnvironmentPicker.ManageRequested = opened.Add;
        var toolbar = new AppToolbarControl();
        var window = Show(toolbar, vm);
        try
        {
            vm.OpenRequestCommand.Execute(request);
            Dispatcher.UIThread.RunJobs();
            var picker = toolbar.FindControl<ComboBox>("CollectionEnvironmentPicker")!;
            var active = orders.ActiveEnvironmentId;

            picker.SelectedItem = vm.CollectionEnvironmentPicker.ManageAction;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { orders }, opened);
            Assert.Equal(active, orders.ActiveEnvironmentId);
            Assert.Equal("Manage Orders environments...", vm.CollectionEnvironmentPicker.ManageAction.Text);
            Assert.Same(vm.CollectionEnvironmentPicker.SelectedChoice, picker.SelectedItem);

            Assert.Equal("Use 'prod' in every collection", vm.CollectionEnvironmentPicker.UseInEveryCollectionAction.Text);
            picker.SelectedItem = vm.CollectionEnvironmentPicker.UseInEveryCollectionAction;
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(vm.CollectionEnvironmentPicker.UseInEveryCollectionReport);
            Assert.Equal(active, orders.ActiveEnvironmentId);
        }
        finally { window.Close(); }
    });
}
