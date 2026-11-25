using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;
using ApiConnectTool.Tests.ViewModels;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views;
using ApiConnectTool.Views.Components;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class RequestEditorLayoutHeadlessTests
{
    [Fact]
    public Task EditorHasExactlySevenTabsInOrder() => HeadlessSession.RunAsync(() =>
    {
        var tab = new RequestTabViewModel();
        tab.QueryParams.Add(new KeyValueItem { Key = "page", Value = "1" });
        var control = new RequestEditorControl { DataContext = tab };
        var window = new Window { Content = control, Width = 1200, Height = 700 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var tabs = control.FindControl<TabControl>("EditorTabControl")!.Items.OfType<TabItem>().Select(item => item.Header).ToArray();
            Assert.Equal(new object?[]
            {
                "Params (1)", AppLocalization.Get("Str.RequestTabs.Authorization"), "Headers (0)",
                AppLocalization.Get("Str.RequestTabs.Body"), "Assertions (0)", AppLocalization.Get("Str.Scripts.Tab"),
                AppLocalization.Get("Str.RequestTabs.Settings")
            }, tabs);
            Assert.DoesNotContain("Pre-request Script", tabs);
            Assert.DoesNotContain("Tests", tabs);
            Assert.Null(control.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Name == "InspectVariablesButton"));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task MethodPickerColorsEachMethodAndBodyModeIsARadioRow() => HeadlessSession.RunAsync(() =>
    {
        var tab = new RequestTabViewModel();
        var control = new RequestEditorControl { DataContext = tab };
        var window = new Window { Content = control, Width = 1200, Height = 700 };
        try
        {
            window.Show();
            var picker = control.FindControl<ComboBox>("MethodPicker")!;
            Assert.Equal(RequestTabViewModel.Methods, picker.Items.Cast<string>());
            picker.SelectedIndex = 1;
            Assert.Equal("POST", tab.Method);

            control.FindControl<TabControl>("EditorTabControl")!.SelectedItem = control.FindControl<TabItem>("BodyTab");
            window.UpdateLayout();
            var modes = control.FindControl<StackPanel>("BodyModeButtons")!.Children.OfType<RadioButton>().ToArray();
            Assert.Equal(5, modes.Length);
            Assert.True(modes[1].IsChecked);
            modes[2].IsChecked = true;
            Assert.Equal(RequestBodyMode.FormData, tab.BodyMode);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ProductionStripAppearsForAProductionCollectionEnvironment() => HeadlessSession.RunAsync(() =>
    {
        var prod = new RequestEnvironment { Name = "prod", IsProduction = true };
        var request = new RequestItem { Name = "List orders", Url = "https://orders.test" };
        var orders = new RequestCollection { Name = "Orders", Environments = { prod }, Requests = { request } };
        var vm = new MainWindowViewModel(new FakeWorkspaceService
        {
            WorkspaceToReturn = new Workspace { Collections = new ObservableCollection<RequestCollection> { orders } }
        });
        vm.OpenRequestCommand.Execute(request);
        var control = new RequestEditorControl { DataContext = vm.ActiveTab };
        var window = new Window { Content = control, DataContext = vm, Width = 1200, Height = 700 };
        try
        {
            window.Show();
            var strip = control.FindControl<Border>("ProductionStrip")!;
            Assert.False(strip.IsVisible);

            orders.ActiveEnvironmentId = prod.Id;
            vm.SaveWorkspaceToDisk();
            Dispatcher.UIThread.RunJobs();
            Assert.True(strip.IsVisible);
            Assert.Contains(strip.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("'prod'") == true);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ScriptsTypedInTheScriptsTabPersistAfterSaveAndReload() => HeadlessSession.RunAsync(async () =>
    {
        var request = new RequestItem { Name = "Scripted", Url = "https://example.test" };
        var service = new FakeWorkspaceService
        {
            WorkspaceToReturn = new Workspace
            {
                Collections = new ObservableCollection<RequestCollection> { new() { Name = "Api", Requests = { request } } }
            }
        };
        var vm = new MainWindowViewModel(service);
        vm.OpenRequestCommand.Execute(request);
        var window = new MainWindow { DataContext = vm };
        try
        {
            window.Show();
            var editor = window.ActiveRequestEditor!;
            editor.FindControl<TabControl>("EditorTabControl")!.SelectedItem = editor.FindControl<TabItem>("ScriptsTab");
            window.UpdateLayout();
            editor.FindControl<TextBox>("PreRequestScriptEditor")!.Text = "pm.variables.set('a', 1);";
            editor.FindControl<TextBox>("TestScriptEditor")!.Text = "pm.test('ok', () => {});";
            await vm.SaveCommand.ExecuteAsync(null);
        }
        finally { window.Close(); }

        var reloaded = new MainWindowViewModel(new FakeWorkspaceService { WorkspaceToReturn = service.LastSaved! });
        reloaded.OpenRequestCommand.Execute(reloaded.CollectionsPanel.Collections.Single().Requests.Single());
        Assert.Equal("pm.variables.set('a', 1);", reloaded.ActiveTab!.AssertionTests.PreRequestScript);
        Assert.Equal("pm.test('ok', () => {});", reloaded.ActiveTab.AssertionTests.TestScript);
    });
}
