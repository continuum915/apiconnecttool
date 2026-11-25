using Avalonia.Controls;
using Avalonia.VisualTree;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.Services;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class VariableOverridePersistenceHeadlessTests
{
    [Theory]
    [InlineData("globals")]
    [InlineData("environment")]
    [InlineData("collectionVariables")]
    public Task EditsResetAndScriptsPersistAndReachHttp(string scope) => HeadlessSession.RunAsync(async () =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "apiconnecttool-overrides-" + Guid.NewGuid());
        var service = new WorkspaceService(folder);
        var row = new EnvironmentVariable { Key = "value", InitialValue = "default", CurrentValue = "legacy", IsSecret = true };
        var workspace = new Workspace();
        var collection = new RequestCollection(); workspace.Collections.Add(collection);
        var environment = new RequestEnvironment(); workspace.Environments.Add(environment);
        var rows = scope == "globals" ? workspace.GlobalVariables : scope == "environment" ? environment.Variables : collection.Variables;
        rows.Add(row);
        Action save = () => service.SaveWorkspace(workspace);
        var editor = new EnvironmentEditorViewModel(workspace, save);
        if (scope == "environment") editor.SelectedEnvironment = environment;
        Window window = scope == "collectionVariables"
            ? new CollectionSettingsWindow { DataContext = new CollectionSettingsViewModel(collection, save), Width = 1200 }
            : new EnvironmentEditorWindow { DataContext = editor, Width = 1200 };
        string? sent = null;
        var execution = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        {
            sent = message.RequestUri!.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        })));
        async Task AssertSent(string expected)
        {
            save();
            var loaded = service.LoadWorkspace();
            var map = VariableScopeBuilder.Build(loaded, loaded.Collections[0], loaded.Environments[0]);
            await execution.ExecuteAsync(new RequestItem { Url = "https://example.test/{{value}}" }, new() { Variables = map.Variables });
            Assert.Equal("/" + expected, sent);
        }
        try
        {
            window.Show(); window.UpdateLayout();
            Assert.False(row.HasCurrentValue); // Rendering a legacy override must not migrate it by editing.
            var use = window.GetVisualDescendants().OfType<CheckBox>().Single(x => Equals(Avalonia.Automation.AutomationProperties.GetName(x), "Use override"));
            Assert.True(use.IsChecked);
            await AssertSent("legacy");
            window.GetVisualDescendants().OfType<TextBox>().Single(x => x.Text == "default").Text = "edited-default";
            var current = window.GetVisualDescendants().OfType<TextBox>().Single(x => x.Text == "legacy");
            current.Text = "edited-override";
            await AssertSent("edited-override");
            current.Text = "";
            await AssertSent("");
            use.IsChecked = false;
            await AssertSent("edited-default");
            use.IsChecked = true;
            await AssertSent("");
            var reset = window.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "Reset override"));
            reset.Command!.Execute(reset.CommandParameter);
            await AssertSent("edited-default");
            Assert.False(reset.IsEnabled);
            var scripts = new ScriptVariableScopes(workspace, collection, environment, save);
            scripts.Apply(scope, "value", "script-override"); scripts.Save();
            await AssertSent("script-override");
            var stored = File.ReadAllText(Path.Combine(folder, "workspace.json"));
            Assert.DoesNotContain("script-override", stored);
            Assert.DoesNotContain("edited-default", stored);
            rows.Add(new() { Key = "empty", InitialValue = "empty-default", HasCurrentValue = true });
            rows.Add(new() { Key = "disabled", InitialValue = "disabled-default", CurrentValue = "legacy-disabled", IsEnabled = false });
            rows.Add(new() { Key = "plain", InitialValue = "plain-default" });
            var before = rows.Select(x => (x.Id, x.Key, x.InitialValue.Value, x.IsEnabled, x.IsSecret)).ToArray();
            var search = window.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "VariableSearchTextBox");
            search.Text = "plain";
            Assert.Equal("plain", Assert.Single(window.GetVisualDescendants().OfType<DataGrid>().Single().ItemsSource.Cast<EnvironmentVariable>()).Key);
            var bulk = window.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "Reset all overrides..."));
            void Click(Button button) => button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Click(bulk);
            var dialog = Assert.Single(window.OwnedWindows);
            var message = dialog.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Text?.Contains("entire scope") == true).Text!;
            Assert.Contains("3 overrides", message);
            Assert.Contains(scope == "globals" ? "Globals" : scope == "environment" ? environment.Name : collection.Name, message);
            Assert.Contains("hidden by a filter", message);
            Click(dialog.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "Cancel")));
            Assert.Equal("script-override", row.CurrentValue.Value);
            Assert.True(rows[1].HasCurrentValue);
            Assert.Equal("legacy-disabled", rows[2].CurrentValue.Value);
            Click(bulk);
            dialog = Assert.Single(window.OwnedWindows);
            Click(dialog.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "Reset all overrides")));
            Assert.All(rows, x => { Assert.False(x.HasCurrentValue); Assert.Equal("", x.CurrentValue.Value); });
            Assert.Equal(before, rows.Select(x => (x.Id, x.Key, x.InitialValue.Value, x.IsEnabled, x.IsSecret)).ToArray());
            Assert.False(bulk.IsEnabled);
            await AssertSent("edited-default");
            var reloaded = service.LoadWorkspace();
            var persisted = scope == "globals" ? reloaded.GlobalVariables : scope == "environment" ? reloaded.Environments[0].Variables : reloaded.Collections[0].Variables;
            Assert.Equal(before, persisted.Select(x => (x.Id, x.Key, x.InitialValue.Value, x.IsEnabled, x.IsSecret)).ToArray());
            Assert.All(persisted, x => { Assert.False(x.HasCurrentValue); Assert.Equal("", x.CurrentValue.Value); });
            scripts.Apply(scope, "value", null); scripts.Save();
            Assert.DoesNotContain(rows, x => x.Key == "value");
        }
        finally { window.Close(); editor.Dispose(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    });

    [Fact]
    public Task FailedResetSaveKeepsEditsAndCanBeRetried() => HeadlessSession.RunAsync(() =>
    {
        var row = new EnvironmentVariable { Key = "key", InitialValue = "default", CurrentValue = "override" };
        var workspace = new Workspace { GlobalVariables = new() { row } };
        var service = new ApiConnectTool.Tests.ViewModels.FakeWorkspaceService { WorkspaceToReturn = workspace };
        var main = new MainWindowViewModel(service);
        var window = new EnvironmentEditorWindow { DataContext = main.CreateEnvironmentEditor(), Width = 1200 };
        try
        {
            window.Show(); window.UpdateLayout();
            service.SaveException = new IOException("Cannot save overrides");
            window.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "VariableSearchTextBox").Text = "missing";
            var reset = window.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "Reset all overrides..."));
            reset.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            var dialog = Assert.Single(window.OwnedWindows);
            dialog.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "Reset all overrides"))
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Cannot save overrides", main.WorkspaceSaveError);
            Assert.False(row.HasCurrentValue);
            Assert.Equal("", row.CurrentValue.Value);
            service.SaveException = null;
            Assert.True(main.SaveWorkspaceToDisk());
            Assert.Null(main.WorkspaceSaveError);
            Assert.Same(workspace, service.LastSaved);
        }
        finally { window.Close(); }
    });
}
