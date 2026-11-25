using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Headless;
using Avalonia.Input;
using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views;
using ApiConnectTool.Views.Components;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.Services;
using ApiConnectTool.Tests.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class EnvironmentSearchHeadlessTests
{
    private static void Click(Window window, string content) => window.GetVisualDescendants().OfType<Button>()
        .Single(x => Equals(x.Content, content)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public Task HiddenEnvironmentDeletionNamesTheSelectionAndSaveErrorsRemainRetryable() => HeadlessSession.RunAsync(() =>
    {
        var source = new RequestEnvironment { Name = "Sending target" };
        var other = new RequestEnvironment { Name = "Other" };
        var workspace = new Workspace { Environments = new() { source, other }, SelectedEnvironmentId = source.Id };
        var service = new FakeWorkspaceService { WorkspaceToReturn = workspace };
        var main = new MainWindowViewModel(service);
        var editor = main.CreateEnvironmentEditor();
        var window = new EnvironmentEditorWindow { DataContext = editor };
        try
        {
            window.Show(); window.UpdateLayout();
            editor.SelectedEnvironment = other;
            var search = window.FindControl<TextBox>("EnvironmentSearchTextBox")!;
            search.Text = "missing";
            Click(window, "Delete...");
            var dialog = Assert.Single(window.OwnedWindows);
            Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), x => x.Text?.Contains("Other") == true);
            Click(dialog, "Cancel");
            Assert.Contains(other, workspace.Environments);
            Assert.Same(other, editor.SelectedEnvironment);
            service.SaveException = new IOException("Cannot save filtered environments");
            Click(window, "Delete...");
            Click(Assert.Single(window.OwnedWindows), "Delete");
            Assert.DoesNotContain(other, workspace.Environments);
            Assert.Same(source, main.SelectedEnvironment);
            Assert.Equal("Cannot save filtered environments", main.WorkspaceSaveError);
            Assert.Equal("missing", search.Text);
            service.SaveException = null;
            Assert.True(main.SaveWorkspaceToDisk());
            Assert.Null(main.WorkspaceSaveError);
            Assert.Equal(source.Id, service.LastSaved!.SelectedEnvironmentId);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("globals")]
    [InlineData("environment")]
    [InlineData("collection")]
    public Task HiddenEditsPersistAndCaseSensitiveResolutionStillReachesHttp(string scope) => HeadlessSession.RunAsync(async () =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "apiconnecttool-search-" + Guid.NewGuid());
        var service = new WorkspaceService(folder);
        var environment = new RequestEnvironment { Name = "Sending environment" };
        var collection = new RequestCollection { Name = "Collection" };
        var workspace = new Workspace { Environments = new() { environment }, Collections = new() { collection }, SelectedEnvironmentId = environment.Id };
        var rows = scope == "globals" ? workspace.GlobalVariables : scope == "environment" ? environment.Variables : collection.Variables;
        rows.Add(new() { Key = "Token", InitialValue = "secret-default", IsSecret = true });
        rows.Add(new() { Key = "token", InitialValue = "lowercase" });
        service.SaveWorkspace(workspace);
        string? sent = null;
        var execution = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        {
            sent = message.RequestUri!.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        })));
        var main = new MainWindowViewModel(service, execution);
        var manager = main.CreateEnvironmentEditor();
        if (scope == "globals") manager.SelectedEnvironment = manager.Globals;
        var savedCollection = main.CollectionsPanel.Collections[0];
        Window window = scope == "collection"
            ? new CollectionSettingsWindow { DataContext = new CollectionSettingsViewModel(savedCollection, () => main.SaveWorkspaceToDisk()), Width = 1200 }
            : new EnvironmentEditorWindow { DataContext = manager, Width = 1200 };
        try
        {
            window.Show(); window.UpdateLayout();
            var control = window.GetVisualDescendants().OfType<VariableEditorControl>().Single();
            control.GetVisualDescendants().OfType<TextBox>().Single(x => x.Text == "secret-default").Text = "saved-secret-edit";
            var search = control.FindControl<TextBox>("VariableSearchTextBox")!;
            search.Text = "TOKEN";
            var grid = control.GetVisualDescendants().OfType<DataGrid>().Single();
            Assert.Equal(2, grid.ItemsSource.Cast<EnvironmentVariable>().Count());
            search.Text = "saved-secret-edit";
            Assert.Empty(grid.ItemsSource.Cast<EnvironmentVariable>());
            if (window is EnvironmentEditorWindow)
            {
                window.FindControl<TextBox>("EnvironmentSearchTextBox")!.Text = "missing";
                Assert.Same(scope == "globals" ? manager.Globals : main.SelectedEnvironment, manager.SelectedEnvironment);
            }
            Assert.Equal(environment.Id, main.SelectedEnvironment!.Id);
            Assert.True(main.SaveWorkspaceToDisk());
            var loaded = service.LoadWorkspace();
            Assert.Equal(environment.Id, loaded.SelectedEnvironmentId);
            var map = VariableScopeBuilder.Build(loaded, loaded.Collections[0], loaded.Environments[0]);
            await execution.ExecuteAsync(new RequestItem { Url = "https://example.test/{{Token}}/{{token}}" }, new() { Variables = map.Variables });
            Assert.Equal("/saved-secret-edit/lowercase", sent);
            Assert.DoesNotContain("saved-secret-edit", File.ReadAllText(Path.Combine(folder, "workspace.json")));
            search.Text = "";
            Assert.Equal(2, grid.ItemsSource.Cast<EnvironmentVariable>().Count());
        }
        finally { window.Close(); manager.Dispose(); Directory.Delete(folder, true); }
    });

    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Win7Light)]
    public Task FilteredActionsAndKeyboardWorkWithLongNames(AppTheme theme) => HeadlessSession.RunAsync(() =>
    {
        var app = Avalonia.Application.Current!;
        var previous = app.RequestedThemeVariant;
        var themeManager = new AppThemeManager(app);
        themeManager.Apply(theme);
        var source = new RequestEnvironment { Name = new string('A', 160), Variables = new() { new() { Key = "hidden", CurrentValue = "override" } } };
        var conflict = new RequestEnvironment { Name = source.Name + " copy" };
        var workspace = new Workspace { Environments = new() { source, conflict }, SelectedEnvironmentId = source.Id };
        var main = new MainWindowViewModel(new FakeWorkspaceService { WorkspaceToReturn = workspace });
        var editor = main.CreateEnvironmentEditor();
        var window = new EnvironmentEditorWindow { DataContext = editor, Width = 600 };
        try
        {
            window.Show(); window.UpdateLayout();
            var search = window.FindControl<TextBox>("EnvironmentSearchTextBox")!;
            search.Focus();
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.NotSame(search, window.FocusManager!.GetFocusedElement());
            search.Text = "missing";
            Assert.True(window.FindControl<TextBlock>("OutsideFilterText")!.IsVisible);
            Click(window, "Duplicate...");
            var dialog = Assert.Single(window.OwnedWindows);
            Assert.Equal(source.Name + " copy 2", dialog.GetVisualDescendants().OfType<TextBox>().Single().Text);
            Click(dialog, "Duplicate");
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.Equal("", search.Text);
            Assert.Same(editor.SelectedEnvironment, window.GetVisualDescendants().OfType<ListBox>().Single().SelectedItem);
            Assert.Same(source, main.SelectedEnvironment);
            search.Text = "missing";
            window.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "Add Environment")).Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Equal("", search.Text);
            Assert.Equal("New Environment", editor.SelectedEnvironment!.Name);
            var control = window.GetVisualDescendants().OfType<VariableEditorControl>().Single();
            Assert.True(control.FindControl<TextBlock>("EmptyVariablesText")!.IsVisible);
            control.FindControl<TextBox>("VariableSearchTextBox")!.Text = "missing";
            Assert.True(control.FindControl<TextBlock>("EmptyVariablesText")!.IsVisible);
            Assert.False(control.FindControl<TextBlock>("NoMatchingVariablesText")!.IsVisible);
            Click(window, "Add Variable");
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var addedKey = control.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "VariableKeyTextBox");
            Assert.Same(addedKey, window.FocusManager!.GetFocusedElement());
            addedKey.Text = new string('K', 160);
            Assert.Equal(new string('K', 160), editor.SelectedEnvironment.Variables.Single().Key);
            Assert.True(search.Bounds.Width > 100);
            Assert.True(control.FindControl<TextBox>("VariableSearchTextBox")!.Bounds.Width > 100);
            Assert.True(addedKey.Bounds.Width > 80);
            Assert.All(control.GetVisualDescendants().OfType<DataGrid>().Single().Columns, column => Assert.True(column.ActualWidth >= 80));
            Assert.Same(source, main.SelectedEnvironment);
        }
        finally { window.Close(); themeManager.Apply(AppTheme.Light); app.RequestedThemeVariant = previous; }
    });

    [Fact]
    public Task KeySearchNeverMatchesValuesAndAddClearsSearchAndFocusesKey() => HeadlessSession.RunAsync(() =>
    {
        var row = new EnvironmentVariable { Key = "Token", InitialValue = "private-match", CurrentValue = "edited-secret", IsSecret = true };
        using var editor = new VariableEditorViewModel(new() { row, new() { Key = "Host" } }, () => { });
        var control = new VariableEditorControl { DataContext = editor };
        var window = new Window { Content = control, Width = 1200, Height = 500 };
        try
        {
            window.Show(); window.UpdateLayout();
            var search = control.FindControl<TextBox>("VariableSearchTextBox");
            Assert.NotNull(search);
            var grid = control.GetVisualDescendants().OfType<DataGrid>().Single();
            search.Text = "tOk";
            Assert.Same(row, Assert.Single(grid.ItemsSource.Cast<EnvironmentVariable>()));
            search.Text = "private-match";
            Assert.Empty(grid.ItemsSource.Cast<EnvironmentVariable>());
            Assert.True(control.FindControl<TextBlock>("NoMatchingVariablesText")!.IsVisible);
            Assert.False(control.FindControl<TextBlock>("EmptyVariablesText")!.IsVisible);
            search.Text = "";
            Assert.Equal(2, grid.ItemsSource.Cast<EnvironmentVariable>().Count());
            Assert.Equal("edited-secret", row.CurrentValueText);
            editor.Variables.Move(0, 1);
            Assert.Equal(new[] { "Host", "Token" }, grid.ItemsSource.Cast<EnvironmentVariable>().Select(x => x.Key));
            search.Text = "missing";
            control.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "Add Variable"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.Equal("", search.Text);
            var added = editor.Variables.Last();
            var key = control.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "VariableKeyTextBox" && ReferenceEquals(x.DataContext, added));
            Assert.Same(key, window.FocusManager!.GetFocusedElement());
            key.Text = "NewKey";
            Assert.Equal("NewKey", added.Key);
        }
        finally { window.Close(); }
    });
    [Fact]
    public Task SearchHidesNamesWithoutLosingSelectionOrEdits() => HeadlessSession.RunAsync(() =>
    {
        var source = new RequestEnvironment { Name = "Production", Variables = new() { new() { Key = "Token", InitialValue = "private-match", IsSecret = true } } };
        var other = new RequestEnvironment { Name = "Development" };
        var editor = new EnvironmentEditorViewModel(new Workspace { Environments = new() { source, other } }, () => { }) { SelectedEnvironment = source };
        var window = new EnvironmentEditorWindow { DataContext = editor };
        try
        {
            window.Show(); window.UpdateLayout();
            var search = window.FindControl<TextBox>("EnvironmentSearchTextBox");
            Assert.NotNull(search);
            source.Variables[0].CurrentValueText = "retained-edit";
            search.Text = "DEVELOP";
            window.UpdateLayout();
            Assert.Equal(new[] { other }, window.GetVisualDescendants().OfType<ListBox>().Single().Items.Cast<RequestEnvironment>());
            Assert.Same(source, editor.SelectedEnvironment);
            Assert.Equal("Production", window.FindControl<TextBox>("EnvironmentNameTextBox")!.Text);
            Assert.True(window.FindControl<TextBlock>("OutsideFilterText")!.IsVisible);
            search.Text = "";
            window.UpdateLayout();
            Assert.Equal(3, window.GetVisualDescendants().OfType<ListBox>().Single().ItemCount);
            Assert.Same(source, window.GetVisualDescendants().OfType<ListBox>().Single().SelectedItem);
            Assert.Same(source, editor.SelectedEnvironment);
            Assert.Equal("retained-edit", source.Variables[0].CurrentValueText);
        }
        finally { window.Close(); }
    });
}
