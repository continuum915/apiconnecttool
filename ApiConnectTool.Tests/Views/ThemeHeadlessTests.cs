using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.ViewModels;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class ThemeHeadlessTests
{
    [Fact]
    public Task SidebarNamesKeepReadableContrastWhenSwitchingThemes() => HeadlessSession.RunAsync(() =>
    {
        var app = Application.Current!;
        var originalVariant = app.RequestedThemeVariant;
        var manager = new AppThemeManager(app);
        using var harness = new SidebarHeadlessHarness(collection =>
            collection.Requests.Add(new RequestItem { Name = "Root contrast request" }));
        try
        {
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Win7Light, AppTheme.Dark, AppTheme.Light })
            {
                manager.Apply(theme);
                Dispatcher.UIThread.RunJobs();
                harness.Window.UpdateLayout();
                Assert.True(harness.Sidebar.TryFindResource("ApiConnectTool.Brush.WindowBackground",
                    harness.Window.ActualThemeVariant, out var resource));
                var background = Assert.IsAssignableFrom<ISolidColorBrush>(resource).Color;
                var labels = harness.Sidebar.GetVisualDescendants().OfType<TextBlock>()
                    .Where(label => label.IsEffectivelyVisible &&
                        (label.DataContext is RequestItem request && label.Text == request.Name ||
                         label.DataContext is FolderTreeNode folder && label.Text == folder.Folder.Name ||
                         label.DataContext is CollectionTreeNode collection && label.Text == collection.Collection.Name))
                    .ToArray();
                Assert.NotEmpty(labels);
                foreach (var label in labels)
                {
                    var foreground = Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color;
                    var light = Math.Max(Luminance(foreground), Luminance(background));
                    var dark = Math.Min(Luminance(foreground), Luminance(background));
                    Assert.True((light + 0.05) / (dark + 0.05) >= 4.5,
                        $"{theme}: '{label.Text}' uses {foreground} on {background}");
                }
            }
        }
        finally
        {
            manager.Apply(AppTheme.Light);
            app.RequestedThemeVariant = originalVariant;
        }
    });

    private static double Luminance(Color color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    [Fact]
    public Task PreferencesSwitchExistingWindowsAndPersistTheme() => HeadlessSession.RunAsync(() =>
    {
        var app = Application.Current!;
        var originalVariant = app.RequestedThemeVariant;
        var originalStyleCount = app.Styles.Count;
        var manager = new AppThemeManager(app);
        var service = new FakeWorkspaceService();
        var viewModel = new MainWindowViewModel(service) { ThemeChangedHandler = manager.Apply };
        var window = new SettingsWindow { DataContext = viewModel };
        try
        {
            window.Show();
            var selector = window.FindControl<ComboBox>("ThemeSelector")!;
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light, AppTheme.Win7Light, AppTheme.Dark, AppTheme.Win7Light, AppTheme.Light })
            {
                selector.SelectedIndex = (int)theme;
                Assert.Equal(theme, viewModel.Theme);
                Assert.Equal(theme, service.LastSaved!.Theme);
                Assert.Equal(theme == AppTheme.Dark ? ThemeVariant.Dark : ThemeVariant.Light, window.ActualThemeVariant);
                Assert.Equal(originalStyleCount + (theme == AppTheme.Win7Light ? 2 : 0), app.Styles.Count);
                Assert.True(window.TryFindResource("ApiConnectTool.Brush.Text", window.ActualThemeVariant, out var text));
                Assert.True(window.TryFindResource("ApiConnectTool.Brush.WindowBackground", window.ActualThemeVariant, out var background));
                var foreground = Assert.IsType<SolidColorBrush>(text).Color;
                var surface = Assert.IsType<SolidColorBrush>(background).Color;
                Assert.True(theme == AppTheme.Dark ? foreground.R > surface.R : foreground.R < surface.R);
                if (theme == AppTheme.Win7Light)
                    Assert.Equal(Color.Parse("#FFF3F7FB"), surface);
                else
                    Assert.False(window.TryFindResource("Win7.Brush.Text", window.ActualThemeVariant, out _));
            }
            Assert.Equal(AppTheme.Light, new MainWindowViewModel(service).Theme);
        }
        finally
        {
            window.Close();
            manager.Apply(AppTheme.Light);
            app.RequestedThemeVariant = originalVariant;
        }
    });
}

