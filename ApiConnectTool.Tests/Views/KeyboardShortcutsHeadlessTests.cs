using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.Services;
using ApiConnectTool.Tests.ViewModels;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views;
using ApiConnectTool.Views.Components;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class KeyboardShortcutsHeadlessTests
{
    [Fact]
    public Task ShortcutTable_IsTheOnlyPlaceGesturesAreDefined_AndPopulatesMenuGesturesAndDescriptions() =>
        HeadlessSession.RunAsync(() =>
        {
            foreach (var entry in KeyboardShortcuts.Table)
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.Description));
                Assert.NotEqual(entry.DescriptionKey, entry.Description);
            }

            var vm = new MainWindowViewModel(new FakeWorkspaceService());
            var window = new MainWindow { DataContext = vm };
            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.Equal(KeyboardShortcuts.Table.Count, window.KeyBindings.Count);
                Assert.Equal(KeyboardShortcuts.Get(ShortcutId.NewTab).Gesture, window.FindControl<MenuItem>("NewRequestMenuItem")!.InputGesture);
                Assert.Equal(KeyboardShortcuts.Get(ShortcutId.SaveRequest).Gesture, window.FindControl<MenuItem>("SaveRequestMenuItem")!.InputGesture);
                Assert.Equal(KeyboardShortcuts.Get(ShortcutId.SaveRequestAs).Gesture, window.FindControl<MenuItem>("SaveRequestAsMenuItem")!.InputGesture);
                Assert.Equal(KeyboardShortcuts.Get(ShortcutId.OpenEnvironments).Gesture, window.FindControl<MenuItem>("EnvironmentsMenuItem")!.InputGesture);
                Assert.Equal(KeyboardShortcuts.Get(ShortcutId.OpenHelp).Gesture, window.FindControl<MenuItem>("ApiConnectToolHelpMenuItem")!.InputGesture);
            }
            finally
            {
                window.Close();
            }

            var repoRoot = FindRepositoryRoot();
            var appDir = Path.Combine(repoRoot, "ApiConnectTool");
            foreach (var axamlPath in Directory.EnumerateFiles(appDir, "*.axaml", SearchOption.AllDirectories))
            {
                if (IsBuildOutput(axamlPath)) continue;
                var text = File.ReadAllText(axamlPath);
                Assert.DoesNotContain("<KeyBinding", text, StringComparison.Ordinal);
                Assert.DoesNotContain("InputGesture=", text, StringComparison.Ordinal);
            }

            foreach (var csPath in Directory.EnumerateFiles(appDir, "*.cs", SearchOption.AllDirectories))
            {
                if (IsBuildOutput(csPath)) continue;
                var relative = Path.GetRelativePath(appDir, csPath).Replace('\\', '/');
                if (relative.Equals("Views/KeyboardShortcuts.cs", StringComparison.OrdinalIgnoreCase)) continue;
                var text = File.ReadAllText(csPath);
                Assert.DoesNotContain("new KeyGesture(", text, StringComparison.Ordinal);
                Assert.DoesNotContain("KeyGesture.Parse(", text, StringComparison.Ordinal);
            }
        });

    [Fact]
    public Task CtrlEnter_InsideBodyEditor_SendsRequestWithoutInsertingNewline() =>
        HeadlessSession.RunAsync(async () =>
        {
            var sendCount = 0;
            var execution = new RequestExecutionService(new TestHttpClientFactory(
                new FakeHttpMessageHandler(_ =>
                {
                    sendCount++;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"ok\":true}")
                    });
                })));
            var vm = new MainWindowViewModel(new FakeWorkspaceService(), execution);
            vm.AddTabCommand.Execute(null);
            vm.ActiveTab!.RequestUrl = "https://api.example.test/items";
            vm.ActiveTab.SelectedMethodIndex = 1; // POST
            vm.ActiveTab.RequestBody = "{\"name\":\"apiconnecttool\"}";

            var window = new MainWindow { DataContext = vm };
            try
            {
                window.Show();
                window.UpdateLayout();

                var editor = window.ActiveRequestEditor!;
                var tabControl = editor.GetVisualDescendants().OfType<TabControl>().First();
                tabControl.SelectedIndex = 3; // Body tab
                window.UpdateLayout();

                var bodyBox = editor.FindControl<TextBox>("BodyTextBox")!;
                Assert.True(bodyBox.Focus());
                Assert.True(bodyBox.IsFocused);

                PressShortcut(window, Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, "\r");
                await WaitForSendCompletionAsync(vm.ActiveTab);

                Assert.Equal(1, sendCount);
                Assert.Equal("{\"name\":\"apiconnecttool\"}", bodyBox.Text);
                Assert.Equal("{\"name\":\"apiconnecttool\"}", vm.ActiveTab.RequestBody);
                Assert.Contains("200", vm.ActiveTab.ResponseStatus);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task Escape_CancelsRunningSend_WhileFocusIsInEditorOrGrid() =>
        HeadlessSession.RunAsync(async () =>
        {
            var sendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var execution = new RequestExecutionService(new TestHttpClientFactory(
                new FakeHttpMessageHandler(_ =>
                {
                    sendStarted.TrySetResult();
                    return new TaskCompletionSource<HttpResponseMessage>().Task;
                })));
            var vm = new MainWindowViewModel(new FakeWorkspaceService(), execution);
            vm.AddTabCommand.Execute(null);
            vm.ActiveTab!.RequestUrl = "https://api.example.test/slow";

            var window = new MainWindow { DataContext = vm };
            try
            {
                window.Show();
                window.UpdateLayout();

                var editor = window.ActiveRequestEditor!;
                var grid = editor.FindControl<DataGrid>("QueryParamsGrid")!;
                Assert.True(grid.Focus());

                PressShortcut(window, Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter);
                await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(vm.ActiveTab.IsSending);

                PressShortcut(window, Key.Escape, RawInputModifiers.None, PhysicalKey.Escape);
                await WaitForSendCompletionAsync(vm.ActiveTab);

                Assert.False(vm.ActiveTab.IsSending);
                Assert.Equal("Request canceled.", vm.ActiveTab.ResponseBody);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task TabManagementFocusAndEnvironmentShortcuts_WorkWhileFocusIsInEditorOrGrid() =>
        HeadlessSession.RunAsync(() =>
        {
            var vm = new MainWindowViewModel(new FakeWorkspaceService());
            var prompts = new List<IReadOnlyList<RequestTabViewModel>>();
            var promptChoice = UnsavedChangesChoice.Cancel;
            vm.UnsavedChangesPromptHandler = tabs =>
            {
                prompts.Add(tabs);
                return Task.FromResult(promptChoice);
            };

            var window = new MainWindow { DataContext = vm };
            try
            {
                window.Show();
                window.UpdateLayout();

                // Ctrl+T opens first tab
                PressShortcut(window, Key.T, RawInputModifiers.Control, PhysicalKey.T);
                Assert.Single(vm.OpenRequests);
                var firstTab = vm.ActiveTab!;
                firstTab.Name = "Tab 1";
                firstTab.IsDirty = false;
                window.UpdateLayout();

                // Focus inside the Params DataGrid and press Ctrl+T to open a second tab
                var editor = window.ActiveRequestEditor!;
                var grid = editor.FindControl<DataGrid>("QueryParamsGrid")!;
                Assert.True(grid.Focus());
                PressShortcut(window, Key.T, RawInputModifiers.Control, PhysicalKey.T);
                Assert.Equal(2, vm.OpenRequests.Count);
                var secondTab = vm.ActiveTab!;
                secondTab.Name = "Tab 2";
                secondTab.IsDirty = false;

                // Focus inside the Body editor and use Ctrl+Tab / Ctrl+Shift+Tab to switch tabs
                var tabControl = editor.GetVisualDescendants().OfType<TabControl>().First();
                tabControl.SelectedIndex = 3; // Body tab
                window.UpdateLayout();
                var bodyBox = editor.FindControl<TextBox>("BodyTextBox")!;
                Assert.True(bodyBox.Focus());

                PressShortcut(window, Key.Tab, RawInputModifiers.Control, PhysicalKey.Tab);
                Assert.Same(firstTab, vm.ActiveTab);

                PressShortcut(window, Key.Tab, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.Tab);
                Assert.Same(secondTab, vm.ActiveTab);

                // Ctrl+L focuses the URL box from inside the Body editor
                Assert.True(bodyBox.Focus());
                PressShortcut(window, Key.L, RawInputModifiers.Control, PhysicalKey.L);
                var urlBox = editor.FindControl<TextBox>("UrlTextBox")!;
                Assert.True(urlBox.IsFocused);

                // Ctrl+S saves the active tab
                secondTab.RequestUrl = "https://api.example.test/save";
                PressShortcut(window, Key.S, RawInputModifiers.Control, PhysicalKey.S);
                Assert.False(secondTab.IsDirty);
                Assert.NotNull(secondTab.RequestId);

                // Ctrl+Shift+S saves a copy
                PressShortcut(window, Key.S, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.S);
                Assert.Equal(2, vm.CollectionsPanel.Collections.SelectMany(c => c.Requests).Count());

                // Mark secondTab dirty and press Ctrl+W: Cancel keeps the tab open, Discard closes it
                secondTab.RequestUrl = "https://api.example.test/dirty";
                Assert.True(secondTab.IsDirty);
                promptChoice = UnsavedChangesChoice.Cancel;
                PressShortcut(window, Key.W, RawInputModifiers.Control, PhysicalKey.W);
                Assert.Single(prompts);
                Assert.Equal(2, vm.OpenRequests.Count);

                promptChoice = UnsavedChangesChoice.Discard;
                PressShortcut(window, Key.W, RawInputModifiers.Control, PhysicalKey.W);
                Assert.Equal(2, prompts.Count);
                Assert.Single(vm.OpenRequests);
                Assert.Same(firstTab, vm.ActiveTab);

                // Ctrl+E opens the EnvironmentEditorWindow
                PressShortcut(window, Key.E, RawInputModifiers.Control, PhysicalKey.E);
                var envWindow = Assert.Single(window.OwnedWindows.OfType<EnvironmentEditorWindow>());
                envWindow.Close();
            }
            finally
            {
                window.Close();
            }
        });

    private static void PressShortcut(
        TopLevel window,
        Key key,
        RawInputModifiers modifiers,
        PhysicalKey physicalKey,
        string? keySymbol = null)
    {
        window.KeyPress(key, modifiers, physicalKey, keySymbol);
        window.KeyRelease(key, modifiers, physicalKey, keySymbol);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task WaitForSendCompletionAsync(RequestTabViewModel tab)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (tab.IsSending && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static bool IsBuildOutput(string path) =>
        path.Split('/', '\\').Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase));

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ApiConnectTool.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate ApiConnectTool.slnx.");
    }
}
