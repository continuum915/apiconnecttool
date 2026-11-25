using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Text.Json.Nodes;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.Services;
using ApiConnectTool.Tests.ViewModels;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class EnvironmentDuplicationHeadlessTests
{
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    private static Button Button(Window window, string name) => window.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, name));
    private static CheckBox Option(Window dialog, string name) => dialog.GetVisualDescendants().OfType<CheckBox>().Single(x => Equals(x.Content, name));

    [Fact]
    public Task DuplicateDefaultsExcludeSecretsAndOverrides() => HeadlessSession.RunAsync(() =>
    {
        var source = new RequestEnvironment { Name = "Production", IsProduction = true, Variables = new()
        {
            new() { Key = "host", InitialValue = "default-host", CurrentValue = "override-host" },
            new() { Key = "token", InitialValue = "secret-default", CurrentValue = "secret-override", IsSecret = true, IsEnabled = false }
        } };
        var workspace = new Workspace { Environments = new() { source } };
        var saves = 0;
        var editor = new EnvironmentEditorViewModel(workspace, () => saves++) { SelectedEnvironment = source };
        var window = new EnvironmentEditorWindow { DataContext = editor };
        try
        {
            window.Show(); window.UpdateLayout();
            Click(Button(window, "Duplicate..."));
            var dialog = Assert.Single(window.OwnedWindows);
            Assert.All(dialog.GetVisualDescendants().OfType<CheckBox>(), x => Assert.False(x.IsChecked));
            Click(Button(dialog, "Duplicate"));
            var copy = Assert.IsType<RequestEnvironment>(editor.SelectedEnvironment);
            Assert.NotSame(source, copy);
            Assert.NotEqual(source.Id, copy.Id);
            Assert.Equal("Production copy", copy.Name);
            Assert.True(copy.IsProduction);
            Assert.Equal("default-host", copy.Variables[0].InitialValue.Value);
            Assert.All(copy.Variables, x => { Assert.Equal("", x.CurrentValue.Value); Assert.False(x.HasCurrentValue); });
            Assert.Equal("", copy.Variables[1].InitialValue.Value);
            Assert.True(copy.Variables[1].IsSecret);
            Assert.False(copy.Variables[1].IsEnabled);
            Assert.NotEqual(source.Variables[0].Id, copy.Variables[0].Id);
            Assert.NotEqual(source.Variables[1].Id, copy.Variables[1].Id);
            Assert.Equal(1, saves);
            copy.Variables[0].InitialValueText = "edited-copy";
            Assert.Equal("default-host", source.Variables[0].InitialValue.Value);
            Assert.Equal("secret-override", source.Variables[1].CurrentValue.Value);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task OptionsPersistIndependentCopiesWithoutChangingSendingEnvironment(bool overrides, bool secrets) => HeadlessSession.RunAsync(async () =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "apiconnecttool-duplicate-" + Guid.NewGuid());
        var service = new WorkspaceService(folder);
        var source = new RequestEnvironment { Name = "Source", IsProduction = true, Variables = new()
        {
            new() { Key = "legacy", InitialValue = "default-legacy", CurrentValue = "override-legacy" },
            new() { Key = "empty", InitialValue = "default-empty", HasCurrentValue = true },
            new() { Key = "secret", InitialValue = "secret-default", CurrentValue = "secret-override", IsSecret = true },
            new() { Key = "secret-empty", InitialValue = "secret-empty-default", HasCurrentValue = true, IsSecret = true, IsEnabled = false },
            new() { Key = "plain", InitialValue = "plain-default", IsEnabled = false },
            new() { Key = "explicit", InitialValue = "explicit-default", CurrentValue = "explicit-override", HasCurrentValue = true },
            new() { Key = "secret-legacy", InitialValue = "secret-legacy-default", CurrentValue = "secret-legacy-override", IsSecret = true }
        } };
        var workspace = new Workspace { Environments = new() { source }, SelectedEnvironmentId = source.Id, ProductionWarningMode = ProductionWarningMode.NeverWarn };
        service.SaveWorkspace(workspace);
        var urls = new List<string>();
        var execution = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        {
            urls.Add(message.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        })));
        var main = new MainWindowViewModel(service, execution);
        var original = main.SelectedEnvironment!;
        var editor = main.CreateEnvironmentEditor();
        var window = new EnvironmentEditorWindow { DataContext = editor, Width = 1200 };
        try
        {
            window.Show(); window.UpdateLayout();
            Click(Button(window, "Duplicate..."));
            var dialog = Assert.Single(window.OwnedWindows);
            Option(dialog, "Copy overrides").IsChecked = overrides;
            Option(dialog, "Include secret values").IsChecked = secrets;
            Click(Button(dialog, "Duplicate"));
            var copy = editor.SelectedEnvironment!;
            Assert.Same(original, main.SelectedEnvironment);
            Assert.NotEqual(original.Id, copy.Id);
            Assert.True(copy.IsProduction);
            Assert.Equal(7, copy.Variables.Count);
            Assert.Equal(7, copy.Variables.Select(x => x.Id).Distinct().Count());
            Assert.DoesNotContain(copy.Variables, x => original.Variables.Any(y => y.Id == x.Id));
            Assert.Equal(original.Variables.Select(x => (x.Key, x.IsSecret, x.IsEnabled)), copy.Variables.Select(x => (x.Key, x.IsSecret, x.IsEnabled)));
            Assert.Equal("default-legacy", copy.Variables[0].InitialValue.Value);
            Assert.Equal(overrides ? "override-legacy" : "", copy.Variables[0].CurrentValue.Value);
            Assert.Equal(overrides, copy.Variables[0].UseOverride);
            Assert.Equal(overrides, copy.Variables[1].HasCurrentValue);
            Assert.Equal("", copy.Variables[1].CurrentValue.Value);
            Assert.Equal(secrets ? "secret-default" : "", copy.Variables[2].InitialValue.Value);
            Assert.Equal(secrets && overrides ? "secret-override" : "", copy.Variables[2].CurrentValue.Value);
            Assert.Equal(secrets && overrides, copy.Variables[3].HasCurrentValue);
            Assert.Equal(secrets ? "secret-empty-default" : "", copy.Variables[3].InitialValue.Value);
            Assert.Equal("plain-default", copy.Variables[4].InitialValue.Value);
            Assert.False(copy.Variables[4].UseOverride);
            Assert.Equal(overrides ? "explicit-override" : "", copy.Variables[5].CurrentValue.Value);
            Assert.Equal(overrides, copy.Variables[5].HasCurrentValue);
            Assert.Equal(secrets && overrides ? "secret-legacy-override" : "", copy.Variables[6].CurrentValue.Value);
            Assert.Equal(secrets && overrides, copy.Variables[6].UseOverride);
            var loaded = service.LoadWorkspace();
            Assert.Equal(original.Id, loaded.SelectedEnvironmentId);
            var persisted = loaded.Environments.Single(x => x.Id == copy.Id);
            Assert.Equal(copy.Variables.Select(x => (x.Id, x.Key, x.InitialValue.Value, x.CurrentValue.Value, x.HasCurrentValue, x.IsSecret, x.IsEnabled)),
                persisted.Variables.Select(x => (x.Id, x.Key, x.InitialValue.Value, x.CurrentValue.Value, x.HasCurrentValue, x.IsSecret, x.IsEnabled)));
            var json = File.ReadAllText(Path.Combine(folder, "workspace.json"));
            Assert.Contains("ENCRYPTED:", json);
            Assert.DoesNotContain("secret-default", json);
            Assert.DoesNotContain("secret-override", json);
            copy.Variables[0].InitialValueText = "copy-only";
            copy.Variables[0].UseOverride = false;
            Assert.Equal("default-legacy", original.Variables[0].InitialValue.Value);
            Assert.Equal("override-legacy", original.Variables[0].CurrentValue.Value);
            main.AddTabCommand.Execute(null);
            main.ActiveTab!.RequestUrl = "https://example.test/{{legacy}}";
            await main.ActiveTab.SendCommand.ExecuteAsync(null);
            Assert.Equal("/override-legacy", Assert.Single(urls));
            main.SelectedEnvironment = copy;
            await main.ActiveTab.SendCommand.ExecuteAsync(null);
            Assert.Equal("/copy-only", urls.Last());
        }
        finally { window.Close(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    });

    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Win7Light)]
    public Task NamingCancellationAndKeyboardFocusWorkInSupportedAppearances(AppTheme theme) => HeadlessSession.RunAsync(() =>
    {
        var app = Avalonia.Application.Current!;
        var previousTheme = app.RequestedThemeVariant;
        var manager = new AppThemeManager(app);
        manager.Apply(theme);
        var longName = new string('A', 160);
        var source = new RequestEnvironment { Name = longName };
        var workspace = new Workspace { Environments = new() { source, new() { Name = longName.ToLowerInvariant() + " COPY" } } };
        var saves = 0;
        var editor = new EnvironmentEditorViewModel(workspace, () => saves++);
        var window = new EnvironmentEditorWindow { DataContext = editor };
        try
        {
            window.Show(); window.UpdateLayout();
            Assert.False(Button(window, "Duplicate...").IsEnabled); // Globals
            Assert.Null(editor.CreateDuplication());
            editor.SelectedEnvironment = source;
            Click(Button(window, "Duplicate..."));
            var dialog = Assert.Single(window.OwnedWindows);
            var name = dialog.GetVisualDescendants().OfType<TextBox>().Single();
            Assert.Equal(longName + " copy 2", name.Text);
            Assert.Same(name, dialog.FocusManager!.GetFocusedElement());
            Assert.All(dialog.GetVisualDescendants().OfType<CheckBox>(), x => Assert.False(x.IsChecked));
            var focusOrder = new List<object?>();
            for (var i = 0; i < 4; i++)
            {
                dialog.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                dialog.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                focusOrder.Add(dialog.FocusManager.GetFocusedElement());
            }
            Assert.Equal(new object[] { Option(dialog, "Copy overrides"), Option(dialog, "Include secret values"), Button(dialog, "Cancel"), Button(dialog, "Duplicate") }, focusOrder);
            name.Text = "   ";
            Assert.False(Button(dialog, "Duplicate").IsEnabled);
            Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == "Enter a nonblank environment name.");
            name.Text = "  " + longName.ToLowerInvariant() + "  ";
            Assert.False(Button(dialog, "Duplicate").IsEnabled);
            Click(Button(dialog, "Cancel"));
            Assert.Equal(2, workspace.Environments.Count);
            Assert.Equal(0, saves);
            Assert.Same(source, editor.SelectedEnvironment);
            Click(Button(window, "Duplicate..."));
            dialog = Assert.Single(window.OwnedWindows);
            name = dialog.GetVisualDescendants().OfType<TextBox>().Single();
            name.Text = "  Trimmed copy  ";
            Click(Button(dialog, "Duplicate"));
            Assert.Equal("Trimmed copy", editor.SelectedEnvironment!.Name);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, saves);
            Assert.Same(editor.SelectedEnvironment, window.GetVisualDescendants().OfType<ListBox>().Single().SelectedItem);
            var editingName = window.FindControl<TextBox>("EnvironmentNameTextBox")!;
            Assert.Same(editingName, window.FocusManager!.GetFocusedElement());
            Assert.Equal("Trimmed copy", editingName.SelectedText);
        }
        finally { window.Close(); manager.Apply(AppTheme.Light); app.RequestedThemeVariant = previousTheme; }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FailedSecretDecryptionLeavesWorkspaceUntouchedAndCanRetryWithoutSecrets(bool failedOverride) => HeadlessSession.RunAsync(() =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "apiconnecttool-duplicate-decrypt-" + Guid.NewGuid());
        var service = new WorkspaceService(folder);
        var source = new RequestEnvironment { Name = "Source", Variables = new()
        {
            new() { Key = "valid", InitialValue = "valid-default" },
            new() { Key = "secret", IsSecret = true, InitialValue = "secret-default", CurrentValue = "secret-override", HasCurrentValue = true }
        } };
        var workspace = new Workspace { Environments = new() { source } };
        service.SaveWorkspace(workspace);
        var path = Path.Combine(folder, "workspace.json");
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        json["Environments"]![0]!["Variables"]![1]![failedOverride ? "CurrentValue" : "InitialValue"] = "ENCRYPTED:invalid";
        File.WriteAllText(path, json.ToJsonString());
        workspace = service.LoadWorkspace();
        source = workspace.Environments[0];
        var saves = 0;
        var editor = new EnvironmentEditorViewModel(workspace, () => saves++) { SelectedEnvironment = source };
        var window = new EnvironmentEditorWindow { DataContext = editor };
        try
        {
            window.Show(); window.UpdateLayout();
            Click(Button(window, "Duplicate..."));
            var dialog = Assert.Single(window.OwnedWindows);
            Option(dialog, "Include secret values").IsChecked = true;
            Option(dialog, "Copy overrides").IsChecked = true;
            Click(Button(dialog, "Duplicate"));
            Assert.Same(dialog, Assert.Single(window.OwnedWindows));
            Assert.Single(workspace.Environments);
            Assert.Equal(0, saves);
            Assert.Same(source, editor.SelectedEnvironment);
            Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), x => x.Text?.Contains("could not be decrypted") == true && x.Text.Contains("Uncheck Include secret values"));
            Option(dialog, "Include secret values").IsChecked = false;
            Click(Button(dialog, "Duplicate"));
            var copy = editor.SelectedEnvironment!;
            Assert.Equal(2, workspace.Environments.Count);
            Assert.Equal(1, saves);
            Assert.Equal("valid-default", copy.Variables[0].InitialValue.Value);
            Assert.Equal("", copy.Variables[1].InitialValue.Value);
            Assert.Equal("", copy.Variables[1].CurrentValue.Value);
            Assert.False(copy.Variables[1].HasCurrentValue);
            Assert.True(copy.Variables[1].IsSecret);
            Assert.True(failedOverride ? source.Variables[1].CurrentValue.FailedToDecrypt : source.Variables[1].InitialValue.FailedToDecrypt);
        }
        finally { window.Close(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    });

    [Fact]
    public Task FailedSaveKeepsCreatedCopyAndSendingSelectionForRetry() => HeadlessSession.RunAsync(() =>
    {
        var source = new RequestEnvironment { Name = "Source", Variables = new() { new() { Key = "value", InitialValue = "default" } } };
        var workspace = new Workspace { Environments = new() { source }, SelectedEnvironmentId = source.Id };
        var service = new FakeWorkspaceService { WorkspaceToReturn = workspace };
        var main = new MainWindowViewModel(service);
        var editor = main.CreateEnvironmentEditor();
        var window = new EnvironmentEditorWindow { DataContext = editor };
        try
        {
            window.Show(); window.UpdateLayout();
            service.SaveException = new IOException("Cannot save duplicated environment");
            Click(Button(window, "Duplicate..."));
            Click(Button(Assert.Single(window.OwnedWindows), "Duplicate"));
            Assert.Equal("Cannot save duplicated environment", main.WorkspaceSaveError);
            Assert.Equal(2, workspace.Environments.Count);
            var copy = editor.SelectedEnvironment!;
            Assert.NotSame(source, copy);
            Assert.Same(source, main.SelectedEnvironment);
            service.SaveException = null;
            Assert.True(main.SaveWorkspaceToDisk());
            Assert.Null(main.WorkspaceSaveError);
            Assert.Contains(copy, service.LastSaved!.Environments);
            Assert.Equal(source.Id, service.LastSaved.SelectedEnvironmentId);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(ProductionWarningMode.AlwaysWarn, false, 2, 0)]
    [InlineData(ProductionWarningMode.AlwaysWarn, true, 2, 2)]
    [InlineData(ProductionWarningMode.WarnOncePerSession, false, 2, 0)]
    [InlineData(ProductionWarningMode.WarnOncePerSession, true, 1, 2)]
    [InlineData(ProductionWarningMode.NeverWarn, false, 0, 2)]
    public Task CopiedProductionEnvironmentKeepsSingleSendAndCollectionRunPolicies(ProductionWarningMode mode, bool proceed, int expectedPrompts, int expectedSends) => HeadlessSession.RunAsync(async () =>
    {
        var source = new RequestEnvironment { Name = "Production", IsProduction = true };
        var workspace = new Workspace { Environments = new() { source }, SelectedEnvironmentId = source.Id, ProductionWarningMode = mode };
        var sends = 0;
        var execution = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
        {
            sends++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        })));
        var main = new MainWindowViewModel(new FakeWorkspaceService { WorkspaceToReturn = workspace }, execution);
        var editor = main.CreateEnvironmentEditor();
        var window = new EnvironmentEditorWindow { DataContext = editor };
        try
        {
            window.Show(); window.UpdateLayout();
            Click(Button(window, "Duplicate..."));
            Click(Button(Assert.Single(window.OwnedWindows), "Duplicate"));
            var copy = editor.SelectedEnvironment!;
            var prompts = 0;
            main.ProductionSendConfirmationHandler = targets => { Assert.Same(copy, Assert.Single(targets).Environment); prompts++; return Task.FromResult(proceed); };
            main.SelectedEnvironment = copy;
            main.AddTabCommand.Execute(null);
            main.ActiveTab!.RequestUrl = "https://example.test/single";
            await main.ActiveTab.SendCommand.ExecuteAsync(null);
            var collection = new RequestCollection { Requests = new() { new() { Url = "https://example.test/run" } } };
            var runner = main.CreateCollectionRunner(collection);
            await runner.StartCommand.ExecuteAsync(null);
            Assert.Equal(expectedPrompts, prompts);
            Assert.Equal(expectedSends, sends);
            Assert.Equal(expectedSends == 0 ? CollectionRunEndState.Cancelled : CollectionRunEndState.Completed, runner.Report!.EndState);
        }
        finally { window.Close(); }
    });
}
