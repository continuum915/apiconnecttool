using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.ViewModels;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class ConsoleWindowHeadlessTests
{
    private static ConsoleLog Seed()
    {
        var log = new ConsoleLog();
        log.Add(new ConsoleEntry(ConsoleEntryKind.Network, DateTimeOffset.UtcNow, "GET https://a.test",
            new ConsoleNetworkDetails("GET", "https://a.test", 404, TimeSpan.FromMilliseconds(12), "Accept: */*", "Server: test", "not found", false)));
        log.Add(new ConsoleEntry(ConsoleEntryKind.Warning, DateTimeOffset.UtcNow, "careful"));
        log.Add(new ConsoleEntry(ConsoleEntryKind.Error, DateTimeOffset.UtcNow, "broken"));
        return log;
    }

    [Fact]
    public Task FiltersExpandAndTimestamps() => HeadlessSession.RunAsync(() =>
    {
        var vm = new ConsoleWindowViewModel(Seed());
        var window = new ConsoleWindow { DataContext = vm };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var rows = window.FindControl<ItemsControl>("ConsoleRows")!;
            Assert.Equal(3, rows.ItemCount);

            window.FindControl<RadioButton>("FilterErrors")!.IsChecked = true;
            window.FindControl<RadioButton>("FilterErrors")!.Command!.Execute(ConsoleFilter.Errors);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, rows.ItemCount);
            Assert.Equal("1 of 3 shown", window.FindControl<TextBlock>("ConsoleShownText")!.Text);

            vm.Filter = ConsoleFilter.All;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var details = rows.GetVisualDescendants().OfType<StackPanel>().Single(panel => panel.Name == "NetworkDetails" && panel.DataContext is ConsoleRow { IsNetwork: true });
            Assert.False(details.IsVisible);
            var chevron = rows.GetVisualDescendants().OfType<Button>().First(button => button.Classes.Contains("chevron") && button.IsVisible);
            chevron.Command!.Execute(chevron.CommandParameter);
            Dispatcher.UIThread.RunJobs();
            Assert.True(details.IsVisible);
            Assert.Contains(details.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Server: test");
            chevron.Command!.Execute(chevron.CommandParameter);
            Dispatcher.UIThread.RunJobs();
            Assert.False(details.IsVisible);

            var time = rows.GetVisualDescendants().OfType<TextBlock>().First(text => text.Classes.Contains("muted") && text.Text?.Contains(':') == true);
            Assert.True(time.IsVisible);
            window.FindControl<CheckBox>("TimestampsCheckBox")!.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(time.IsVisible);

            var errorRow = rows.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("console-row") && border.Classes.Contains("error"));
            Assert.True(window.TryFindResource("ApiConnectTool.Brush.DangerBackground", window.ActualThemeVariant, out var danger));
            Assert.Same(danger, errorRow.Background);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task OpensFromTheViewMenuAndStaysSingle() => HeadlessSession.RunAsync(() =>
    {
        var main = new MainWindow { DataContext = new MainWindowViewModel(new FakeWorkspaceService()) };
        try
        {
            main.Show();
            main.FindControl<MenuItem>("ConsoleMenuItem")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            main.FindControl<MenuItem>("ToolsConsoleMenuItem")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Single(main.OwnedWindows.OfType<ConsoleWindow>()).Close();
        }
        finally { main.Close(); }
    });
}
