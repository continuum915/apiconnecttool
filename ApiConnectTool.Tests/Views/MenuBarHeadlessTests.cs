using Avalonia.Controls;
using Avalonia.Threading;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;
using ApiConnectTool.Tests.ViewModels;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class MenuBarHeadlessTests
{
    private static readonly string[] TopLevelHeaders =
        ["Str.Menu.File", "Str.Menu.Edit", "Str.Menu.View", "Str.Menu.Collection", "Str.Menu.Request", "Str.Menu.Tools", "Str.Menu.Help"];

    [Fact]
    public Task MenuBar_HasTheClassicMenusWithLocalizedHeadersAndUniqueAccessKeys() => HeadlessSession.RunAsync(() =>
    {
        var window = new MainWindow { DataContext = new MainWindowViewModel(new FakeWorkspaceService()) };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var menu = window.FindControl<Menu>("MainMenu")!;
            var topLevel = menu.Items.OfType<MenuItem>().ToArray();
            Assert.Equal(TopLevelHeaders.Select(AppLocalization.Get), topLevel.Select(item => item.Header));
            AssertUniqueAccessKeys(topLevel);

            foreach (var item in topLevel)
                AssertMenu(item);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task ThemeItems_ReflectTheCurrentTheme() => HeadlessSession.RunAsync(() =>
    {
        var vm = new MainWindowViewModel(new FakeWorkspaceService());
        var window = new MainWindow { DataContext = vm };
        try
        {
            window.Show();
            vm.Theme = AppTheme.Dark;
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.FindControl<MenuItem>("ThemeDarkMenuItem")!.IsChecked);
            Assert.False(window.FindControl<MenuItem>("ThemeLightMenuItem")!.IsChecked);
            vm.Theme = AppTheme.Light;
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.FindControl<MenuItem>("ThemeLightMenuItem")!.IsChecked);
            Assert.False(window.FindControl<MenuItem>("ThemeDarkMenuItem")!.IsChecked);
        }
        finally
        {
            vm.Theme = AppTheme.Light;
            window.Close();
        }
    });

    [Fact]
    public Task RequestMenuShowsTheShortcutGestures() => HeadlessSession.RunAsync(() =>
    {
        var window = new MainWindow { DataContext = new MainWindowViewModel(new FakeWorkspaceService()) };
        try
        {
            window.Show();
            Assert.Equal(KeyboardShortcuts.Get(ShortcutId.SendRequest).Gesture, window.FindControl<MenuItem>("SendMenuItem")!.InputGesture);
            Assert.Equal(KeyboardShortcuts.Get(ShortcutId.CancelSend).Gesture, window.FindControl<MenuItem>("CancelMenuItem")!.InputGesture);
            Assert.Equal(KeyboardShortcuts.Get(ShortcutId.CloseTab).Gesture, window.FindControl<MenuItem>("CloseTabMenuItem")!.InputGesture);
        }
        finally
        {
            window.Close();
        }
    });

    private static void AssertMenu(MenuItem menu)
    {
        var items = menu.Items.OfType<MenuItem>().ToArray();
        Assert.NotEmpty(items);
        foreach (var item in items)
        {
            var header = Assert.IsType<string>(item.Header);
            Assert.False(string.IsNullOrWhiteSpace(header));
            Assert.False(header.StartsWith("Str.", StringComparison.Ordinal), $"Missing resource for {header}");
            Assert.Contains('_', header);
            if (item.Items.Count > 0) AssertMenu(item);
        }
        AssertUniqueAccessKeys(items);
    }

    private static void AssertUniqueAccessKeys(IEnumerable<MenuItem> items)
    {
        var keys = items.Select(item => (string)item.Header!)
            .Select(header => char.ToUpperInvariant(header[header.IndexOf('_') + 1]))
            .ToArray();
        Assert.Equal(keys.Length, keys.Distinct().Count());
    }
}
