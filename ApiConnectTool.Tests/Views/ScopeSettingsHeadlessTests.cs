using Avalonia.Controls;
using Avalonia.VisualTree;
using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views;
using ApiConnectTool.Views.Components;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class ScopeSettingsHeadlessTests
{
    [Fact]
    public Task VariableGridEditsPersistAndSecretToggleUpdatesMask() => HeadlessSession.RunAsync(() =>
    {
        var row = new EnvironmentVariable { Key = "key", InitialValue = "initial-value", CurrentValue = "current-value" };
        var collection = new RequestCollection { Variables = new() { row } };
        var settings = new CollectionSettingsViewModel(collection, () => { });
        var window = new CollectionSettingsWindow { DataContext = settings };
        try
        {
            window.Show(); window.UpdateLayout();
            var initial = window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Text == "initial-value");
            initial.Text = "edited";
            Assert.Equal("edited", row.InitialValue.Value);
            row.IsSecret = true;
            Assert.Equal('●', initial.PasswordChar);
            settings.VariableEditor.IsSecretVisible = true;
            Assert.Equal('\0', initial.PasswordChar);
        }
        finally { window.Close(); }
    });
    [Fact]
    public Task ScriptWritesAndRunPersistenceStayOnUiThread() => HeadlessSession.RunAsync(async () =>
    {
        var workspace = new Workspace();
        var collection = new RequestCollection { Requests = new() { new() { Url = "https://example.test",
            PreRequestScript = "pm.globals.set('before','value');", TestScript = "pm.globals.set('after','value');" } } };
        workspace.Collections.Add(collection);
        var execution = new ApiConnectTool.Services.RequestExecutionService(new ApiConnectTool.Tests.Services.TestHttpClientFactory(
            new ApiConnectTool.Tests.Services.FakeHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)))));
        var main = new MainWindowViewModel(new ApiConnectTool.Tests.ViewModels.FakeWorkspaceService { WorkspaceToReturn = workspace }, execution);
        workspace.GlobalVariables.CollectionChanged += (_, _) => Assert.True(Avalonia.Threading.Dispatcher.UIThread.CheckAccess());
        main.CollectionsPanel.Tree.CollectionChanged += (_, _) => Assert.True(Avalonia.Threading.Dispatcher.UIThread.CheckAccess());
        var window = new CollectionSettingsWindow { DataContext = main.CreateCollectionSettings(collection) };
        try
        {
            window.Show();
            var runner = main.CreateCollectionRunner(collection);
            await runner.StartCommand.ExecuteAsync(null);
            Assert.Equal(1, runner.Report!.PassedCount);
            Assert.Equal(2, workspace.GlobalVariables.Count);
        }
        finally { window.Close(); }
    });
    [Fact]
    public Task AuthWindowsAndInheritanceLabelBind() => HeadlessSession.RunAsync(() =>
    {
        var collection = new RequestCollection { Name = "API" };
        var folder = new CollectionFolder { Name = "Folder" }; collection.Folders.Add(folder);
        var request = new RequestItem { FolderId = folder.Id }; collection.Requests.Add(request);
        var tab = RequestTabViewModel.FromRequest(request); tab.CollectionProvider = () => collection; tab.RefreshAuthContext();
        var control = new AuthEditorControl { DataContext = tab.AuthEditor };
        var owner = new Window { Content = control };
        var settings = new CollectionSettingsWindow { DataContext = new CollectionSettingsViewModel(collection, () => { }) };
        var folderWindow = new FolderSettingsWindow { DataContext = new FolderSettingsViewModel(folder, collection, () => { }) };
        try
        {
            owner.Show(); settings.Show(); folderWindow.Show();
            var label = control.FindControl<TextBlock>("InheritanceSourceLabel")!;
            Assert.Equal("No auth is set on any parent", label.Text);
            collection.Auth.Type = AuthType.Bearer;
            Assert.Equal("Using Bearer Token from API", label.Text);
            Assert.Equal(2, Assert.IsType<TabControl>(settings.Content).ItemCount);
            Assert.Single(Assert.IsType<TabControl>(folderWindow.Content).Items);
        }
        finally { owner.Close(); settings.Close(); folderWindow.Close(); tab.AuthEditor.Dispose(); }
    });
    [Fact]
    public Task CollectionAndGlobalsUseSharedVariableEditor() => HeadlessSession.RunAsync(() =>
    {
        var collection = new CollectionSettingsWindow { DataContext = new CollectionSettingsViewModel(new(), () => { }) };
        var globals = new EnvironmentEditorWindow { DataContext = new EnvironmentEditorViewModel(new Workspace(), () => { }) };
        try
        {
            collection.Show(); globals.Show();
            Assert.IsType<TabControl>(collection.Content);
            Assert.IsType<EnvironmentEditorViewModel>(globals.DataContext);
            var editor = Assert.IsType<EnvironmentEditorViewModel>(globals.DataContext);
            Assert.Same(editor.Globals, editor.SelectedEnvironment);
        }
        finally { collection.Close(); globals.Close(); }
    });
}
