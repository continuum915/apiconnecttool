using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class RunnerWindowLayoutHeadlessTests
{
    private static CollectionRunEntry Entry(string name, RunEntryOutcome outcome, string body) =>
        new(Guid.NewGuid(), name, 0, outcome == RunEntryOutcome.Errored ? null : 200, TimeSpan.FromMilliseconds(40),
            outcome == RunEntryOutcome.Failed ? [new AssertionResult(Guid.NewGuid(), "status is 201", false, "200", "expected 201")] : [],
            outcome, outcome == RunEntryOutcome.Errored ? "refused" : null)
        {
            Details = new RunEntryDetails("GET", "https://example.test/" + name, "Accept: */*", "Content-Type: text/plain", body, false,
                [new ScriptConsoleLine(ScriptConsoleLevel.Info, "from " + name)])
        };

    private static (CollectionRunnerViewModel Vm, CollectionRunEntry[] Entries) Arrange()
    {
        var collection = new RequestCollection
        {
            Name = "Api",
            Requests = { new() { Name = "a" }, new() { Name = "b" }, new() { Name = "c" }, new() { Name = "d" } }
        };
        var vm = new CollectionRunnerViewModel(collection, new CollectionRunner(RequestRunnerStub()), [], null);
        var entries = new[] { Entry("a", RunEntryOutcome.Passed, "body a"), Entry("b", RunEntryOutcome.Failed, "body b"), Entry("c", RunEntryOutcome.Errored, "") };
        foreach (var entry in entries) vm.Rows.Add(entry);
        vm.Report = new CollectionRunReport(entries, CollectionRunEndState.Completed, TimeSpan.FromMilliseconds(120));
        return (vm, entries);
    }

    private static IRequestRunner RequestRunnerStub() => ApiConnectTool.Tests.Services.RequestRunnerTests.Runner(_ =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)));

    [Fact]
    public Task CountersAndSegmentedProgressFollowTheReport() => HeadlessSession.RunAsync(() =>
    {
        var (vm, _) = Arrange();
        var window = new CollectionRunnerWindow { DataContext = vm };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("3", window.FindControl<TextBlock>("RunCountText")!.Text);
            Assert.Equal("1", window.FindControl<TextBlock>("PassedCountText")!.Text);
            Assert.Equal("1", window.FindControl<TextBlock>("FailedCountText")!.Text);
            Assert.Equal("1", window.FindControl<TextBlock>("ErroredCountText")!.Text);
            Assert.Equal("120 ms", window.FindControl<TextBlock>("TotalTimeText")!.Text);

            var progress = window.FindControl<Grid>("SegmentedProgress")!;
            Assert.Equal(new[] { 1.0, 1.0, 1.0 }, progress.ColumnDefinitions.Take(3).Select(column => column.Width.Value));
            Assert.All(progress.ColumnDefinitions.Take(3), column => Assert.True(column.Width.IsStar));
            Assert.True(window.TryFindResource("ApiConnectTool.Brush.SuccessFill", window.ActualThemeVariant, out var success));
            Assert.Same(success, window.FindControl<Border>("PassedSegment")!.Background);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task DetailsPaneFollowsTheSelectedRowAndItsTabs() => HeadlessSession.RunAsync(() =>
    {
        var (vm, entries) = Arrange();
        var window = new CollectionRunnerWindow { DataContext = vm };
        try
        {
            window.Show();
            var tabs = window.FindControl<TabControl>("RunDetailsTabs")!;
            Assert.False(tabs.IsVisible);

            vm.SelectedEntry = entries[1];
            Dispatcher.UIThread.RunJobs();
            Assert.True(tabs.IsVisible);
            Assert.Single(window.FindControl<DataGrid>("RunDetailsTestsGrid")!.ItemsSource!.Cast<AssertionResult>());

            tabs.SelectedItem = window.FindControl<TabItem>("RunDetailsConsoleTab");
            window.UpdateLayout();
            Assert.Contains(window.FindControl<ItemsControl>("RunDetailsConsole")!.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "from b");

            tabs.SelectedItem = window.FindControl<TabItem>("RunDetailsRequestTab");
            window.UpdateLayout();
            Assert.Equal("https://example.test/b", window.FindControl<SelectableTextBlock>("RunDetailsRequestUrl")!.Text);

            vm.SelectedEntry = entries[0];
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("https://example.test/a", window.FindControl<SelectableTextBlock>("RunDetailsRequestUrl")!.Text);

            tabs.SelectedItem = window.FindControl<TabItem>("RunDetailsResponseTab");
            window.UpdateLayout();
            Assert.Equal("body a", window.FindControl<TextBox>("RunDetailsResponseBody")!.Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task FailedAndErroredRowsUseTheDangerBackground() => HeadlessSession.RunAsync(() =>
    {
        var (vm, _) = Arrange();
        var window = new CollectionRunnerWindow { DataContext = vm };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var rows = window.FindControl<DataGrid>("RunResultsGrid")!.GetVisualDescendants().OfType<DataGridRow>().ToArray();
            Assert.Equal(3, rows.Length);
            Assert.Equal(new[] { false, true, true },
                rows.OrderBy(row => row.Index).Select(row => row.Classes.Contains("problem")));
        }
        finally { window.Close(); }
    });
}
