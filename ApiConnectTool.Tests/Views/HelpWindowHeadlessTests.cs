using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ApiConnectTool.Services.Localization;
using ApiConnectTool.Tests.ViewModels;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class HelpWindowHeadlessTests
{
    [Fact]
    public Task OpenHelp_FollowsTopicLink_SupportsBackAndForward_AndReusesExistingWindow() =>
        HeadlessSession.RunAsync(() =>
        {
            var mainVm = new MainWindowViewModel(new FakeWorkspaceService());
            var mainWindow = new MainWindow { DataContext = mainVm };
            HelpWindow? helpWindow = null;

            try
            {
                mainWindow.Show();
                mainWindow.UpdateLayout();

                var helpMenuItem = mainWindow.FindControl<MenuItem>("ApiConnectToolHelpMenuItem");
                Assert.NotNull(helpMenuItem);
                Assert.Equal(AppLocalization.Get("Str.Menu.Help.Contents"), helpMenuItem.Header);

                helpMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                mainWindow.UpdateLayout();

                helpWindow = Assert.Single(mainWindow.OwnedWindows.OfType<HelpWindow>());
                helpWindow.UpdateLayout();

                Assert.Equal(AppLocalization.Get("Str.Help.WindowTitle"), helpWindow.Title);
                Assert.NotNull(helpWindow.ViewModel);
                Assert.Equal("contents", helpWindow.ViewModel.SelectedTopic?.Id);
                Assert.False(helpWindow.BackButton.IsEnabled);
                Assert.False(helpWindow.ForwardButton.IsEnabled);

                var articleControl = Assert.IsAssignableFrom<Control>(helpWindow.ArticleHost.Content);
                var linkButton = Assert.Single(
                    ApiConnectTool.Tests.Help.HelpMarkdownRendererTests.FindLinkButtons(articleControl),
                    b => Equals(b.Tag, "assertions-and-scripts"));

                linkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                helpWindow.UpdateLayout();

                Assert.Equal("assertions-and-scripts", helpWindow.ViewModel.SelectedTopic?.Id);
                Assert.Equal("assertions-and-scripts", (helpWindow.TopicsListBox.SelectedItem as ApiConnectTool.Help.HelpTopic)?.Id);
                Assert.True(helpWindow.BackButton.IsEnabled);
                Assert.False(helpWindow.ForwardButton.IsEnabled);

                var assertionsArticle = Assert.IsAssignableFrom<Control>(helpWindow.ArticleHost.Content);
                var renderedText = ApiConnectTool.Tests.Help.HelpMarkdownRendererTests.ExtractPlainText(assertionsArticle);
                Assert.Contains("Schema assertions", renderedText);
                Assert.Contains("Request scripts", renderedText);
                Assert.Contains("Sandbox limits", renderedText);

                Assert.True(helpWindow.BackButton.Command!.CanExecute(null));
                helpWindow.BackButton.Command.Execute(null);
                helpWindow.UpdateLayout();
                Assert.Equal("contents", helpWindow.ViewModel.SelectedTopic?.Id);
                Assert.False(helpWindow.BackButton.IsEnabled);
                Assert.True(helpWindow.ForwardButton.IsEnabled);

                Assert.True(helpWindow.ForwardButton.Command!.CanExecute(null));
                helpWindow.ForwardButton.Command.Execute(null);
                helpWindow.UpdateLayout();
                Assert.Equal("assertions-and-scripts", helpWindow.ViewModel.SelectedTopic?.Id);

                var activationsBefore = helpWindow.ActivationCount;
                var secondHelpWindow = mainWindow.OpenHelp();
                Assert.Same(helpWindow, secondHelpWindow);
                Assert.Single(mainWindow.OwnedWindows.OfType<HelpWindow>());
                Assert.Equal(activationsBefore + 1, helpWindow.ActivationCount);
            }
            finally
            {
                helpWindow?.Close();
                mainWindow.Close();
            }
        });

    [Fact]
    public Task HelpWindow_UsesThemeResourcesOnly_AndReadsCorrectlyInLightAndDark() =>
        HeadlessSession.RunAsync(() =>
        {
            var app = Application.Current!;
            var originalTheme = app.RequestedThemeVariant;
            var mainWindow = new MainWindow { DataContext = new MainWindowViewModel(new FakeWorkspaceService()) };
            HelpWindow? helpWindow = null;

            try
            {
                mainWindow.Show();
                helpWindow = mainWindow.OpenHelp("assertions-and-scripts");

                app.RequestedThemeVariant = ThemeVariant.Light;
                helpWindow.UpdateLayout();
                var lightBg = Assert.IsAssignableFrom<ISolidColorBrush>(helpWindow.ArticleScrollViewer.Background).Color;
                var lightHeading = Assert.IsAssignableFrom<Panel>(helpWindow.ArticleHost.Content)
                    .Children.OfType<TextBlock>().First();
                var lightFg = Assert.IsAssignableFrom<ISolidColorBrush>(lightHeading.Foreground).Color;

                app.RequestedThemeVariant = ThemeVariant.Dark;
                helpWindow.UpdateLayout();
                var darkBg = Assert.IsAssignableFrom<ISolidColorBrush>(helpWindow.ArticleScrollViewer.Background).Color;
                var darkHeading = Assert.IsAssignableFrom<Panel>(helpWindow.ArticleHost.Content)
                    .Children.OfType<TextBlock>().First();
                var darkFg = Assert.IsAssignableFrom<ISolidColorBrush>(darkHeading.Foreground).Color;

                Assert.NotEqual(lightBg, darkBg);
                Assert.NotEqual(lightFg, darkFg);
                Assert.True(Luminance(lightBg) > Luminance(lightFg), "Light theme should have dark text on a light surface.");
                Assert.True(Luminance(darkFg) > Luminance(darkBg), "Dark theme should have light text on a dark surface.");
            }
            finally
            {
                app.RequestedThemeVariant = originalTheme;
                helpWindow?.Close();
                mainWindow.Close();
            }

            var repoRoot = FindRepositoryRoot();
            var filesToCheck = new[]
            {
                Path.Combine(repoRoot, "ApiConnectTool", "Views", "HelpWindow.axaml"),
                Path.Combine(repoRoot, "ApiConnectTool", "Views", "HelpWindow.axaml.cs"),
                Path.Combine(repoRoot, "ApiConnectTool", "Views", "HelpMarkdownRenderer.cs")
            };

            foreach (var file in filesToCheck)
            {
                var content = File.ReadAllText(file);
                Assert.DoesNotContain("Brushes.", content, StringComparison.Ordinal);
                Assert.DoesNotContain("Color.From", content, StringComparison.Ordinal);
                Assert.DoesNotContain("Color.Parse", content, StringComparison.Ordinal);
                Assert.DoesNotContain("\"#", content, StringComparison.Ordinal);
            }
        });

    [Fact]
    public Task PressF1_InMainWindowAreas_AndSecondaryWindows_OpensExpectedTopicOrContents() =>
        HeadlessSession.RunAsync(() =>
        {
            var mainVm = new MainWindowViewModel(new FakeWorkspaceService());
            mainVm.AddTabCommand.Execute(null);
            var mainWindow = new MainWindow { DataContext = mainVm };
            HelpWindow? helpWindow = null;

            try
            {
                mainWindow.Show();
                mainWindow.UpdateLayout();

                // 1. Focus on MainWindow outside context areas -> opens "contents"
                mainWindow.Focus();
                PressF1(mainWindow);
                helpWindow = Assert.Single(mainWindow.OwnedWindows.OfType<HelpWindow>());
                Assert.Equal("contents", helpWindow.ViewModel?.SelectedTopic?.Id);

                // 2. Focus in Sidebar search box -> opens "collections"
                var sidebar = mainWindow.GetVisualDescendants().OfType< ApiConnectTool.Views.Components.SidebarControl>().First();
                var searchBox = sidebar.FindControl<TextBox>("CollectionsSearchBox")!;
                Assert.True(searchBox.Focus());
                Dispatcher.UIThread.RunJobs();
                PressF1(mainWindow);
                Assert.Equal("collections", helpWindow.ViewModel?.SelectedTopic?.Id);

                // 3. Focus in RequestEditor URL box -> opens "requests"
                var editor = mainWindow.ActiveRequestEditor!;
                Assert.True(editor.UrlTextBox.Focus());
                Dispatcher.UIThread.RunJobs();
                PressF1(mainWindow);
                Assert.Equal("requests", helpWindow.ViewModel?.SelectedTopic?.Id);

                // 4. Focus in RequestEditor Scripts tab -> opens "assertions-and-scripts"
                var tabControl = editor.FindControl<TabControl>("EditorTabControl")!;
                tabControl.SelectedIndex = 5; // Scripts tab
                mainWindow.UpdateLayout();
                var preRequestEditor = editor.FindControl<TextBox>("PreRequestScriptEditor")!;
                Assert.True(preRequestEditor.Focus());
                Dispatcher.UIThread.RunJobs();
                PressF1(mainWindow);
                Assert.Equal("assertions-and-scripts", helpWindow.ViewModel?.SelectedTopic?.Id);

                // 5. Secondary windows: EnvironmentEditorWindow, CollectionRunnerWindow, SettingsWindow, ImportWizardWindow, AboutWindow
                var secondaryCases = new (Window Window, string ExpectedTopicId)[]
                {
                    (new EnvironmentEditorWindow { DataContext = mainVm.CreateEnvironmentEditor() }, "environments"),
                    (new CollectionRunnerWindow(), "collection-runner"),
                    (new SettingsWindow { DataContext = mainVm }, "preferences"),
                    (new ImportWizardWindow(), "import-and-export"),
                    (new AboutWindow(), "contents")
                };

                foreach (var (secondary, expectedTopicId) in secondaryCases)
                {
                    try
                    {
                        secondary.Show(mainWindow);
                        secondary.UpdateLayout();
                        secondary.Focus();
                        Dispatcher.UIThread.RunJobs();

                        PressF1(secondary);
                        Assert.Equal(expectedTopicId, helpWindow.ViewModel?.SelectedTopic?.Id);
                    }
                    finally
                    {
                        secondary.Close();
                    }
                }
            }
            finally
            {
                helpWindow?.Close();
                mainWindow.Close();
            }
        });

    private static void PressF1(TopLevel window)
    {
        window.KeyPress(Avalonia.Input.Key.F1, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.F1, "F1");
        window.KeyRelease(Avalonia.Input.Key.F1, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.F1, "F1");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static double Luminance(Color color) =>
        0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B;

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "ApiConnectTool.slnx")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate ApiConnectTool.slnx.");
    }
}
