using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ApiConnectTool.Models;
using ApiConnectTool.Tests.ViewModels;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views;
using ApiConnectTool.Views.Components;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class HistoryWindowHeadlessTests
{
    private static MainWindowViewModel Arrange()
    {
        var vm = new MainWindowViewModel(new FakeWorkspaceService(), historyStore: new InMemoryHistoryStore());
        vm.History.Record(new HistoryEntry
        {
            SentAtUtc = DateTimeOffset.UtcNow.AddDays(-30), StatusCode = 404, ReasonPhrase = "Not Found", IsSuccess = true,
            RequestSnapshot = new RequestItem { Name = "old", Url = "https://{{host}}/old" }
        });
        vm.History.Record(new HistoryEntry
        {
            SentAtUtc = DateTimeOffset.UtcNow, StatusCode = 200, ReasonPhrase = "OK", IsSuccess = true, ResponseSize = 2048,
            RequestSnapshot = new RequestItem { Name = "new", Url = "https://{{host}}/new" },
            RedactedResponseBodyPreview = "{\"ok\":true}"
        });
        return vm;
    }

    [Fact]
    public Task GroupSelectionFiltersTheGridAndDetailsFollowTheSelection() => HeadlessSession.RunAsync(() =>
    {
        var vm = Arrange();
        var history = new HistoryWindowViewModel(vm);
        var window = new HistoryWindow { DataContext = history };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var grid = window.FindControl<DataGrid>("HistoryGrid")!;
            Assert.Equal(2, grid.ItemsSource!.Cast<HistoryRow>().Count());

            window.FindControl<ListBox>("HistoryGroups")!.SelectedItem = history.Groups.Single(group => group.Group == HistoryDateGroup.Today);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("new", Assert.Single(grid.ItemsSource!.Cast<HistoryRow>()).Entry.RequestSnapshot.Name);

            history.SelectedGroup = history.Groups[0];
            history.SelectedRow = history.Rows.Single(row => row.Entry.RequestSnapshot.Name == "old");
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("https://{{host}}/old", window.FindControl<TextBox>("HistoryRequestDetails")!.Text);
            Assert.StartsWith("404 Not Found", window.FindControl<TextBlock>("HistoryResponseDetails")!.Text);

            history.SelectedRow = history.Rows.Single(row => row.Entry.RequestSnapshot.Name == "new");
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("https://{{host}}/new", window.FindControl<TextBox>("HistoryRequestDetails")!.Text);
            Assert.Contains("2.0 KB", window.FindControl<TextBlock>("HistoryResponseDetails")!.Text);

            var url = grid.GetVisualDescendants().OfType<TemplateText>().First(text => text.Template == "https://{{host}}/new");
            Assert.Equal(3, url.Inlines!.Count);
            Assert.Equal("2 entries", window.FindControl<TextBlock>("HistoryEntryCount")!.Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task OpensFromTheViewMenuAndStaysSingle() => HeadlessSession.RunAsync(() =>
    {
        var vm = Arrange();
        var main = new MainWindow { DataContext = vm };
        try
        {
            main.Show();
            var item = main.FindControl<MenuItem>("HistoryWindowMenuItem")!;
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var history = Assert.Single(main.OwnedWindows.OfType<HistoryWindow>());
            history.Close();
        }
        finally { main.Close(); }
    });
}
