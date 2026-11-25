using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.Services;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views.Components;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class VariableInspectorHeadlessTests
{
    [Fact]
    public Task NavigatedVariableEditsRefreshSendAndSurviveWorkspaceReload() => HeadlessSession.RunAsync(async () =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "apiconnecttool-inspector-navigation-" + Guid.NewGuid());
        var service = new WorkspaceService(folder);
        var workspace = new Workspace { GlobalVariables = new() { new() { Key = "value", InitialValue = "original" } } };
        var collection = new RequestCollection();
        var request = new RequestItem { Url = "https://example.test/{{value}}" };
        collection.Requests.Add(request); workspace.Collections.Add(collection); service.SaveWorkspace(workspace);
        string? sent = null;
        var main = new MainWindowViewModel(service, new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        {
            sent = message.RequestUri!.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }))));
        main.OpenRequestCommand.Execute(main.CollectionsPanel.Collections[0].Requests[0]);
        var control = new RequestEditorControl { DataContext = main.ActiveTab };
        var owner = new Window { DataContext = main, Content = control, Width = 1400, Height = 800 };
        try
        {
            owner.Show(); owner.UpdateLayout();
            ApiConnectTool.Views.RequestActions.InspectVariables(owner, (RequestTabViewModel)control.DataContext!);
            var dialog = Assert.Single(owner.OwnedWindows); dialog.UpdateLayout();
            var preview = Assert.IsType<VariableInspectorViewModel>(dialog.DataContext);
            var edit = dialog.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "Edit defining scope"));
            Assert.True(edit.Focus());
            dialog.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            dialog.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            var editor = Assert.IsType<ApiConnectTool.Views.EnvironmentEditorWindow>(Assert.Single(dialog.OwnedWindows));
            editor.UpdateLayout();
            editor.GetVisualDescendants().OfType<TextBox>().Single(x => x.Text == "original").Text = "edited";
            Assert.Equal("edited", Assert.Single(preview.Variables).DisplayValue);
            editor.Close();
            Assert.Equal("edited", service.LoadWorkspace().GlobalVariables[0].InitialValue.Value);
            await main.ActiveTab!.SendCommand.ExecuteAsync(null);
            Assert.Equal("/edited", sent);
            dialog.Close();
        }
        finally { owner.Close(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    });

    [Fact]
    public Task OpenInspectorTracksRequestsScopesScriptsAndNavigationWithoutActivatingEditingSelection() => HeadlessSession.RunAsync(async () =>
    {
        var workspace = new Workspace { GlobalVariables = new() { new() { Key = "value", InitialValue = "global" } } };
        var collection = new RequestCollection { Name = "Owner", Variables = new() { new() { Key = "value", InitialValue = "collection" } } };
        var first = new RequestItem { Url = "https://example.test/{{value}}" };
        var second = new RequestItem { Url = "https://example.test/{{other}}" };
        collection.Requests.Add(first); collection.Requests.Add(second); workspace.Collections.Add(collection);
        var environment = new RequestEnvironment { Name = "Local", Variables = new() { new() { Key = "value", InitialValue = "environment", IsSecret = true } } };
        workspace.Environments.Add(environment); workspace.SelectedEnvironmentId = environment.Id;
        var paths = new List<string>();
        var service = new ApiConnectTool.Tests.ViewModels.FakeWorkspaceService { WorkspaceToReturn = workspace };
        var main = new MainWindowViewModel(service, new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        {
            paths.Add(message.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }))));
        main.OpenRequestCommand.Execute(first);
        var firstTab = main.ActiveTab!;
        var control = new RequestEditorControl { DataContext = firstTab };
        var owner = new Window { DataContext = main, Content = control, Width = 1400, Height = 800 };
        try
        {
            owner.Show(); owner.UpdateLayout();
            ApiConnectTool.Views.RequestActions.InspectVariables(owner, (RequestTabViewModel)control.DataContext!);
            var dialog = Assert.Single(owner.OwnedWindows);
            var preview = Assert.IsType<VariableInspectorViewModel>(dialog.DataContext);
            preview.PropertyChanged += (_, _) => Assert.True(Avalonia.Threading.Dispatcher.UIThread.CheckAccess());
            InspectedVariableViewModel Value() => Assert.Single(preview.Variables, x => x.Name == "value");
            preview.RevealSecrets = true;
            Value().EditSourceCommand!.Execute(null);
            var scopeWindow = Assert.IsType<ApiConnectTool.Views.EnvironmentEditorWindow>(Assert.Single(dialog.OwnedWindows));
            var editor = Assert.IsType<EnvironmentEditorViewModel>(scopeWindow.DataContext);
            Assert.Same(environment, editor.SelectedEnvironment);
            editor.SelectedEnvironment = editor.Globals;
            Assert.Same(environment, main.SelectedEnvironment);
            scopeWindow.Close();
            firstTab.AssertionTests.PreRequestScript = "pm.environment.set('value', 'script'); pm.globals.set('written', 'saved');";
            await firstTab.SendCommand.ExecuteAsync(null);
            Assert.Equal("/script", paths.Last());
            Assert.False(preview.RevealSecrets);
            Assert.Equal("••••••", Value().DisplayValue);
            preview.RevealSecrets = true; Assert.Equal("script", Value().DisplayValue);
            firstTab.AssertionTests.PreRequestScript = "";
            var alternate = new RequestEnvironment { Name = "Alternate", Variables = new() { new() { Key = "value", InitialValue = "alternate" } } };
            workspace.Environments.Add(alternate); main.SelectedEnvironment = alternate;
            Assert.Equal("alternate", Value().DisplayValue);
            await firstTab.SendCommand.ExecuteAsync(null);
            Assert.Equal("/alternate", paths.Last());
            main.SelectedEnvironment = null;
            Assert.False(preview.RevealSecrets); Assert.Equal("collection", Value().DisplayValue);
            Value().EditSourceCommand!.Execute(null);
            var settingsWindow = Assert.IsType<ApiConnectTool.Views.CollectionSettingsWindow>(Assert.Single(dialog.OwnedWindows));
            var settings = Assert.IsType<CollectionSettingsViewModel>(settingsWindow.DataContext);
            Assert.Same(collection.Variables, settings.VariableEditor.Variables);
            settingsWindow.Close();
            main.OpenRequestCommand.Execute(second);
            Assert.Equal("other", Assert.Single(preview.Variables).Name);
            firstTab.RequestUrl = "https://example.test/{{stale}}";
            Assert.Equal("other", Assert.Single(preview.Variables).Name);
            var unresolved = Assert.Single(preview.Variables);
            var count = workspace.GlobalVariables.Count;
            unresolved.EditGlobalsCommand!.Execute(null);
            scopeWindow = Assert.IsType<ApiConnectTool.Views.EnvironmentEditorWindow>(Assert.Single(dialog.OwnedWindows));
            editor = Assert.IsType<EnvironmentEditorViewModel>(scopeWindow.DataContext);
            Assert.Same(editor.Globals, editor.SelectedEnvironment);
            Assert.Equal(count, workspace.GlobalVariables.Count);
            service.SaveException = new IOException("Cannot save navigated edits");
            editor.VariableEditor!.AddVariableCommand.Execute(null);
            Assert.Equal("Cannot save navigated edits", main.WorkspaceSaveError);
            service.SaveException = null;
            Assert.True(main.SaveWorkspaceToDisk()); Assert.Null(main.WorkspaceSaveError);
            scopeWindow.Close();
            var tab = main.ActiveTab!;
            tab.Auth = new() { Type = AuthType.Bearer, Token = "{{written}}" };
            Assert.Contains(preview.Variables, x => x.Name == "written" && x.DisplayValue == "saved");
            tab.Auth.Token = "{{newAuth}}";
            Assert.Contains(preview.Variables, x => x.Name == "newAuth");
            tab.RequestUrl = "https://example.test/{{written}}";
            var written = workspace.GlobalVariables.Single(x => x.Key == "written");
            written.IsSecret = true;
            preview.RevealSecrets = true;
            // Keep the UI thread occupied until the worker has queued the notification.
            Task.Run(() => written.CurrentValue = "background").GetAwaiter().GetResult();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(preview.RevealSecrets);
            var refreshedSecret = Assert.Single(preview.Variables, x => x.Name == "written");
            Assert.Equal("••••••", refreshedSecret.DisplayValue);
            preview.RevealSecrets = true; Assert.Equal("background", refreshedSecret.DisplayValue);
            Task.Run(() => written.CurrentValue = "queued-old-context").GetAwaiter().GetResult();
            tab.RequestUrl = "https://example.test/{{replacement}}";
            preview.RevealSecrets = true;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(preview.RevealSecrets);
            Assert.Contains(preview.Variables, x => x.Name == "replacement");
            Task.Run(() => written.CurrentValue = "queued-before-close").GetAwaiter().GetResult();
            dialog.Close();
            var closedNames = preview.Variables.Select(x => x.Name).ToArray();
            tab.RequestUrl = "https://example.test/{{afterClose}}";
            workspace.GlobalVariables.Add(new() { Key = "afterClose", InitialValue = "closed" });
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(preview.RevealSecrets);
            Assert.Equal(closedNames, preview.Variables.Select(x => x.Name));
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task UndecryptableDefinitionsAreUnavailableRatherThanGenuinelyEmpty() => HeadlessSession.RunAsync(() =>
    {
        var workspace = new Workspace { GlobalVariables = new() { new() { Key = "credential", IsSecret = true,
            InitialValue = EncryptedString.Undecryptable(), CurrentValue = EncryptedString.Undecryptable(), HasCurrentValue = true } } };
        var tab = new RequestTabViewModel { WorkspaceProvider = () => workspace, RequestUrl = "https://example.test/{{credential}}" };
        var control = new RequestEditorControl { DataContext = tab };
        var owner = new Window { Content = control, Width = 1400, Height = 800 };
        try
        {
            owner.Show(); owner.UpdateLayout();
            ApiConnectTool.Views.RequestActions.InspectVariables(owner, (RequestTabViewModel)control.DataContext!);
            var dialog = Assert.Single(owner.OwnedWindows);
            var preview = Assert.IsType<VariableInspectorViewModel>(dialog.DataContext);
            var variable = Assert.Single(preview.Variables);
            Assert.Equal("Unavailable (decryption failed)", variable.Status);
            preview.RevealSecrets = true;
            Assert.Equal("Unavailable (decryption failed)", variable.DisplayValue);
            var definition = Assert.Single(variable.Definitions);
            Assert.Equal("Unavailable (decryption failed)", definition.DefaultValue);
            Assert.Equal("Unavailable (decryption failed)", definition.OverrideValue);
            Assert.True(workspace.GlobalVariables[0].InitialValue.FailedToDecrypt);
            dialog.Close();
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task InspectionIsReadOnlyAfterWorkspaceReloadAndScriptsOnlyOverrideOnSend() => HeadlessSession.RunAsync(async () =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "apiconnecttool-inspector-" + Guid.NewGuid());
        var service = new WorkspaceService(folder);
        var savedSecret = Guid.NewGuid().ToString("N");
        var scriptSecret = Guid.NewGuid().ToString("N");
        var workspace = new Workspace { GlobalVariables = new() { new() { Key = "credential", InitialValue = savedSecret, IsSecret = true } } };
        var collection = new RequestCollection { Name = "Owning" };
        var request = new RequestItem { Url = "https://example.test/{{credential}}", Auth = new() { Type = AuthType.Bearer, Token = "{{credential}}" },
            PreRequestScript = "pm.variables.set('credential', '" + scriptSecret + "');",
            TestScript = "throw new Error(pm.variables.get('credential'));" };
        collection.Requests.Add(request); workspace.Collections.Add(collection);
        bool sentScriptSecret = false;
        var execution = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        {
            sentScriptSecret = message.RequestUri!.AbsolutePath == "/" + scriptSecret;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(scriptSecret) });
        })));
        var history = new List<HistoryEntry>();
        Window? owner = null;
        try
        {
            service.SaveWorkspace(workspace);
            var before = File.ReadAllBytes(Path.Combine(folder, "workspace.json"));
            var loaded = service.LoadWorkspace();
            var tab = RequestTabViewModel.FromRequest(loaded.Collections[0].Requests[0], execution, onHistoryRecorded: history.Add);
            tab.WorkspaceProvider = () => loaded; tab.CollectionProvider = () => loaded.Collections[0];
            var control = new RequestEditorControl { DataContext = tab };
            owner = new Window { Content = control, Width = 1400, Height = 800 };
            owner.Show(); owner.UpdateLayout();
            ApiConnectTool.Views.RequestActions.InspectVariables(owner, (RequestTabViewModel)control.DataContext!);
            var dialog = Assert.Single(owner.OwnedWindows);
            var preview = Assert.IsType<VariableInspectorViewModel>(dialog.DataContext);
            var variable = Assert.Single(preview.Variables);
            Assert.Equal("Default", variable.ValueKind);
            Assert.Equal("••••••", variable.DisplayValue);
            preview.RevealSecrets = true;
            Assert.True(variable.DisplayValue == savedSecret);
            Assert.False(sentScriptSecret);
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(folder, "workspace.json")));
            Assert.False(tab.IsDirty);
            dialog.Close();
            Assert.Equal("••••••", variable.DisplayValue);
            Assert.False(tab.BuildCurlCommand(false).Contains(savedSecret, StringComparison.Ordinal));
            Assert.True(tab.BuildCurlCommand(true).Contains(savedSecret, StringComparison.Ordinal));
            await tab.SendCommand.ExecuteAsync(null);
            Assert.True(sentScriptSecret);
            var entry = Assert.Single(history);
            foreach (var diagnostic in new[] { entry.RedactedResponseBodyPreview, entry.RedactedResponseHeadersText,
                entry.ErrorMessage ?? "", tab.AssertionTests.Results.Single().FailureMessage ?? "" })
            {
                Assert.False(diagnostic.Contains(savedSecret, StringComparison.Ordinal));
                Assert.False(diagnostic.Contains(scriptSecret, StringComparison.Ordinal));
            }
            Assert.True(loaded.GlobalVariables[0].InitialValue.Value == savedSecret);
            Assert.False(loaded.GlobalVariables[0].UseOverride);
        }
        finally { owner?.Close(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    });

    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Win7Light)]
    public Task SecretsStayMaskedInEveryDefinitionAndKeyboardRevealResetsOnCloseAndContextChange(AppTheme theme) => HeadlessSession.RunAsync(() =>
    {
        var app = Avalonia.Application.Current!;
        var previous = app.RequestedThemeVariant;
        var manager = new AppThemeManager(app); manager.Apply(theme);
        var name = new string('N', 180);
        var workspace = new Workspace { GlobalVariables = new() { new() { Key = name, InitialValue = "private-default", IsSecret = true } } };
        var collection = new RequestCollection { Name = new string('C', 180), Variables = new() {
            new() { Key = name, InitialValue = "collection-default" },
            new() { Key = name, InitialValue = "disabled-default", CurrentValue = "disabled-override", IsEnabled = false } } };
        var request = new RequestItem { Url = "https://example.test/{{" + name + "}}" };
        collection.Requests.Add(request); workspace.Collections.Add(collection);
        var environment = new RequestEnvironment { Name = new string('E', 180), IsProduction = true, Variables = new() {
            new() { Key = name, InitialValue = "environment-default", CurrentValue = "environment-override" } } };
        workspace.Environments.Add(environment); workspace.SelectedEnvironmentId = environment.Id;
        var workspaceService = new ApiConnectTool.Tests.ViewModels.FakeWorkspaceService { WorkspaceToReturn = workspace };
        var main = new MainWindowViewModel(workspaceService,
            new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK))))));
        main.OpenRequestCommand.Execute(request);
        var productionPrompts = 0;
        main.ProductionSendConfirmationHandler = _ => { productionPrompts++; return Task.FromResult(false); };
        var savesBeforeInspection = workspaceService.SaveCallCount;
        var control = new RequestEditorControl { DataContext = main.ActiveTab };
        var owner = new Window { Content = control, DataContext = main, Width = 1400, Height = 800 };
        try
        {
            owner.Show(); owner.UpdateLayout();
            void Open() => ApiConnectTool.Views.RequestActions.InspectVariables(owner, (RequestTabViewModel)control.DataContext!);
            Open();
            var dialog = Assert.Single(owner.OwnedWindows); dialog.UpdateLayout();
            var preview = Assert.IsType<VariableInspectorViewModel>(dialog.DataContext);
            var variable = Assert.Single(preview.Variables);
            Assert.Equal("••••••", variable.DisplayValue);
            Assert.Equal(4, variable.Definitions.Count);
            Assert.Contains(variable.Definitions, x => x.Status == "Disabled");
            Assert.Contains(variable.Definitions, x => x.Status == "Shadowed");
            Assert.All(variable.Definitions, x => { Assert.Equal("••••••", x.DefaultValue); Assert.Equal("••••••", x.OverrideValue); });
            var reveal = dialog.GetVisualDescendants().OfType<CheckBox>().Single();
            Assert.True(reveal.Focus());
            dialog.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            dialog.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Assert.True(preview.RevealSecrets);
            Assert.True(variable.DisplayValue == environment.Variables[0].CurrentValue.Value);
            Assert.Equal(0, productionPrompts);
            Assert.Equal(savesBeforeInspection, workspaceService.SaveCallCount);
            dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            owner.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Empty(owner.OwnedWindows);
            Assert.False(preview.RevealSecrets);
            Assert.All(variable.Definitions, x => Assert.Equal("••••••", x.DefaultValue));
            Open(); dialog = Assert.Single(owner.OwnedWindows);
            preview = Assert.IsType<VariableInspectorViewModel>(dialog.DataContext); preview.RevealSecrets = true;
            main.SelectedEnvironment = null;
            Assert.Single(owner.OwnedWindows); Assert.False(preview.RevealSecrets);
            preview.RevealSecrets = true;
            workspace.GlobalVariables[0].InitialValue = "updated";
            Assert.Single(owner.OwnedWindows); Assert.False(preview.RevealSecrets);
            main.ActiveTab!.RequestUrl = "https://example.test/no-references";
            dialog.UpdateLayout();
            Assert.True(preview.IsEmpty);
            Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == "This request has no supported variable references.");
            Assert.DoesNotContain(owner.GetVisualDescendants().OfType<TextBox>(), x => x.Text == "updated");
            dialog.Close();
        }
        finally { owner.Close(); manager.Apply(AppTheme.Light); app.RequestedThemeVariant = previous; }
    });

    [Fact]
    public Task UnsavedRequestReferencesEverySupportedFieldWithoutAnImplicitCollection() => HeadlessSession.RunAsync(async () =>
    {
        var workspace = new Workspace { GlobalVariables = new() { new() { Key = "value", InitialValue = "global" } },
            Collections = new() { new() { Name = "Unrelated", Variables = new() { new() { Key = "value", InitialValue = "collection" } } } } };
        string? sent = null;
        var execution = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        {
            sent = message.RequestUri!.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        })));
        var main = new MainWindowViewModel(new ApiConnectTool.Tests.ViewModels.FakeWorkspaceService { WorkspaceToReturn = workspace }, execution);
        main.AddTabCommand.Execute(null);
        var tab = main.ActiveTab!;
        tab.RequestUrl = "https://example.test/{{value}}?{{queryKey}}={{queryValue}}";
        tab.RequestBody = "{{body}} {{ value }}";
        tab.BodyContentType = "{{contentType}}";
        tab.RequestHeaders.Add(new() { Key = "{{headerKey}}", Value = "{{headerValue}}" });
        tab.FormData.Add(new() { Key = "{{formKey}}", Value = "{{formValue}}", FilePath = "{{formFile}}" });
        tab.BodyFilePath = "{{binaryFileNotSupported}}";
        tab.Auth = new() { Type = AuthType.OAuth2, Token = "{{token}}", Username = "{{username}}", Password = "{{password}}",
            ApiKeyHeaderName = "{{apiHeader}}", ApiKeyQueryParam = "{{apiQuery}}", ClientId = "{{client}}", ClientSecret = "{{clientSecret}}",
            AuthorizationUrl = "{{authorizationUrl}}", TokenUrl = "{{tokenUrl}}", Scope = "{{scope}}", RedirectUri = "{{redirect}}" };
        using (var preview = tab.InspectVariables())
        {
            Assert.Equal(new[] { "value", "queryKey", "queryValue", "headerKey", "headerValue", "contentType", "body", "formFile", "formKey", "formValue",
                "token", "username", "password", "apiHeader", "apiQuery", "client", "clientSecret", "authorizationUrl", "tokenUrl", "scope", "redirect" }.Order(),
                preview.Variables.Select(x => x.Name).Order());
            var value = Assert.Single(preview.Variables, x => x.Name == "value");
            Assert.Equal("global", value.DisplayValue);
            Assert.Equal("Default", value.ValueKind);
            Assert.Equal("Globals", value.Source);
            Assert.Single(value.Definitions);
        }
        tab.RequestUrl = "https://example.test/{{value}}";
        tab.RequestHeaders.Clear(); tab.FormData.Clear(); tab.RequestBody = ""; tab.BodyContentType = "application/json"; tab.Auth = new();
        await tab.SendCommand.ExecuteAsync(null);
        Assert.Equal("/global", sent);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SavedPreviewUsesUnsavedEditsAndInheritedAuthAndMatchesHttp(bool inheritFolder) => HeadlessSession.RunAsync(async () =>
    {
        var workspace = new Workspace { GlobalVariables = new() { new() { Key = "value", InitialValue = "global" } } };
        var collection = new RequestCollection { Name = "Owning", Variables = new() { new() { Key = "value", InitialValue = "collection" } },
            Auth = new() { Type = AuthType.Bearer, Token = "{{credential}}" } };
        var environment = new RequestEnvironment { Name = "Local", Variables = new() {
            new() { Key = "value", CurrentValue = "local" },
            new() { Key = "credential", InitialValue = "private", IsSecret = true },
            new() { Key = "empty", InitialValue = "fallback", HasCurrentValue = true } } };
        var request = new RequestItem { Url = "https://example.test/original", Auth = new() { Type = AuthType.Inherit } };
        if (inheritFolder)
        {
            var parent = new CollectionFolder { Name = "Parent", Auth = new() { Type = AuthType.Bearer, Token = "{{credential}}" } };
            var child = new CollectionFolder { Name = "Child", ParentFolderId = parent.Id };
            collection.Folders.Add(parent); collection.Folders.Add(child); request.FolderId = child.Id;
            collection.Auth = new() { Type = AuthType.Bearer, Token = "{{unusedCollectionCredential}}" };
        }
        collection.Requests.Add(request); workspace.Collections.Add(collection);
        string? path = null;
        bool authMatches = false;
        var execution = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        {
            path = message.RequestUri!.AbsolutePath;
            authMatches = message.Headers.Authorization?.Parameter == environment.Variables[1].InitialValue.Value;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        })));
        var tab = RequestTabViewModel.FromRequest(request, execution, () => environment);
        tab.WorkspaceProvider = () => workspace; tab.CollectionProvider = () => collection;
        tab.RequestUrl = "https://example.test/{{value}}/{{empty}}";
        tab.RequestBody = "{{missing}} {{value}}";
        var control = new RequestEditorControl { DataContext = tab };
        var window = new Window { Content = control, Width = 1400, Height = 800 };
        try
        {
            window.Show(); window.UpdateLayout();
            ApiConnectTool.Views.RequestActions.InspectVariables(window, tab);
            var dialog = Assert.Single(window.OwnedWindows);
            var preview = Assert.IsType<VariableInspectorViewModel>(dialog.DataContext);
            Assert.Equal(4, preview.Variables.Count);
            var value = Assert.Single(preview.Variables, x => x.Name == "value");
            Assert.Equal("local", value.DisplayValue);
            Assert.Contains("Local", value.Source);
            Assert.Equal("Override", value.ValueKind);
            Assert.Equal(3, value.Definitions.Count);
            Assert.Equal("(empty)", Assert.Single(preview.Variables, x => x.Name == "empty").DisplayValue);
            Assert.Equal("Unresolved", Assert.Single(preview.Variables, x => x.Name == "missing").Status);
            var secret = Assert.Single(preview.Variables, x => x.Name == "credential");
            Assert.Equal("••••••", secret.DisplayValue);
            var reveal = dialog.GetVisualDescendants().OfType<CheckBox>().Single();
            reveal.IsChecked = true;
            Assert.True(secret.DisplayValue == environment.Variables[1].InitialValue.Value);
            reveal.IsChecked = false;
            Assert.Equal("••••••", secret.DisplayValue);
            await tab.SendCommand.ExecuteAsync(null);
            Assert.Equal("/local/", path); Assert.True(authMatches);
            dialog.Close();
            Assert.Equal("••••••", secret.DisplayValue);
            ApiConnectTool.Views.RequestActions.InspectVariables(window, tab);
            dialog = Assert.Single(window.OwnedWindows);
            preview = Assert.IsType<VariableInspectorViewModel>(dialog.DataContext);
            Assert.False(preview.RevealSecrets);
            preview.RevealSecrets = true;
            tab.RequestBody = "changed";
            Assert.Single(window.OwnedWindows);
            Assert.False(preview.RevealSecrets);
            dialog.Close();
        }
        finally { window.Close(); }
    });
}
