using System.Net;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Search;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.Services;
using ApiConnectTool.Tests.ViewModels;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views;
using ApiConnectTool.Views.Components;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class DailyDriverErgonomicsHeadlessTests
{
    [Fact]
    public Task FolderRunnerTitleNamesTheSelectedFolder() => HeadlessSession.RunAsync(() =>
    {
        var folder = new CollectionFolder { Name = "Chosen folder" };
        var collection = new RequestCollection { Folders = new() { folder } };
        var vm = new CollectionRunnerViewModel(collection, new CollectionRunner(RequestRunnerTests.Runner(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))), [], null, folder);
        var window = new CollectionRunnerWindow { DataContext = vm };
        Assert.Equal("Run Folder: Chosen folder", window.Title);
        window.Close();
    });
    [Fact]
    public Task EnvironmentButtonOpensSelectedEnvironment() => HeadlessSession.RunAsync(() =>
    {
        var first = new RequestEnvironment { Name = "first" };
        var selected = new RequestEnvironment { Name = "selected" };
        var vm = new MainWindowViewModel(new FakeWorkspaceService { WorkspaceToReturn = new() { Environments = new() { first, selected }, SelectedEnvironmentId = selected.Id } });
        var window = new MainWindow { DataContext = vm };
        try
        {
            window.Show(); window.UpdateLayout();
            window.FindControl<MenuItem>("EnvironmentsMenuItem")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var editor = Assert.Single(window.OwnedWindows.OfType<EnvironmentEditorWindow>());
            Assert.Same(selected, Assert.IsType<EnvironmentEditorViewModel>(editor.DataContext).SelectedEnvironment);
            editor.Close();
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ResponseRadioButtonsSwitchViewAndCtrlFSearches() => HeadlessSession.RunAsync(() =>
    {
        var tab = new RequestTabViewModel { ResponseContentType = "application/json", ResponseBody = "{\"n\":1}" };
        var control = new ResponseViewerControl { DataContext = tab };
        var window = new Window { Content = control };
        try
        {
            window.Show(); window.UpdateLayout();
            var editor = control.FindControl<TextEditor>("ResponseEditor")!;
            Assert.Contains("\n", editor.Text);
            var raw = control.GetVisualDescendants().OfType<RadioButton>().Single(button => Equals(button.Content, "Raw"));
            raw.IsChecked = true;
            Assert.True(tab.IsRawResponse);
            Assert.Equal("{\"n\":1}", editor.Text);
            var pretty = control.GetVisualDescendants().OfType<RadioButton>().Single(button => Equals(button.Content, "Pretty"));
            pretty.IsChecked = true;
            Assert.True(tab.IsPrettyResponse);
            Assert.Contains("\n", editor.Text);
            Assert.True(editor.TextArea.Focus());
            window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, null);
            window.KeyRelease(Key.F, RawInputModifiers.Control, PhysicalKey.F, null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(control.GetVisualDescendants().OfType<SearchPanel>().Single().IsClosed);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task FormDataTypeAndPathControlsBindAndDirtyRequest() => HeadlessSession.RunAsync(() =>
    {
        var tab = RequestTabViewModel.FromRequest(new() { BodyMode = RequestBodyMode.FormData,
            FormData = new() { new() { Key = "upload", FieldType = FormDataFieldType.File, FilePath = "C:\\fixture.png" } } });
        var control = new RequestEditorControl { DataContext = tab };
        var window = new Window { Content = control };
        try
        {
            window.Show();
            var tabs = control.GetVisualDescendants().OfType<TabControl>().Single();
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(item => Equals(item.Header, "Body"));
            window.UpdateLayout();
            var path = control.GetVisualDescendants().OfType<TextBox>().Single(box => box.IsEffectivelyVisible && box.Text == "C:\\fixture.png");
            path.Text = "C:\\changed.png";
            Assert.Equal("C:\\changed.png", tab.FormData[0].FilePath);
            Assert.True(tab.IsDirty);
            Assert.Contains(control.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible && Equals(button.Content, "Browse..."));
            var type = control.GetVisualDescendants().OfType<ComboBox>().Single(box => ReferenceEquals(box.DataContext, tab.FormData[0]));
            type.SelectedIndex = 0;
            Assert.Equal(FormDataFieldType.Text, tab.FormData[0].FieldType);
        }
        finally { window.Close(); }
    });
}
