using System.Collections.ObjectModel;
using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class HistoryWindowViewModelTests
{
    // Wednesday 2026-11-18, 09:30 local time.
    private static readonly DateTimeOffset Now = new(new DateTime(2026, 11, 18, 9, 30, 0, DateTimeKind.Local));

    private static DateTimeOffset Local(int day, int hour, int minute = 0) =>
        new DateTimeOffset(new DateTime(2026, 11, day, hour, minute, 0, DateTimeKind.Local)).ToUniversalTime();

    [Theory]
    [InlineData(18, 0, 0, HistoryDateGroup.Today)]
    [InlineData(17, 23, 59, HistoryDateGroup.Yesterday)]
    [InlineData(17, 0, 0, HistoryDateGroup.Yesterday)]
    [InlineData(16, 23, 59, HistoryDateGroup.EarlierThisWeek)]
    [InlineData(16, 0, 0, HistoryDateGroup.EarlierThisWeek)]
    [InlineData(15, 23, 59, HistoryDateGroup.Older)]
    public void Groups_SplitAtLocalMidnight(int day, int hour, int minute, HistoryDateGroup expected) =>
        Assert.Equal(expected, HistoryWindowViewModel.GroupOf(Local(day, hour, minute), Now));

    [Fact]
    public void Monday_HasNoEarlierThisWeek()
    {
        var monday = new DateTimeOffset(new DateTime(2026, 11, 16, 8, 0, 0, DateTimeKind.Local));
        Assert.Equal(HistoryDateGroup.Yesterday, HistoryWindowViewModel.GroupOf(Local(15, 12), monday));
        Assert.Equal(HistoryDateGroup.Older, HistoryWindowViewModel.GroupOf(Local(14, 12), monday));
    }

    private static (MainWindowViewModel Vm, InMemoryHistoryStore Store, RequestItem Saved) Arrange()
    {
        var saved = new RequestItem { Name = "List orders", Url = "https://{{host}}/orders" };
        var store = new InMemoryHistoryStore();
        var vm = new MainWindowViewModel(new FakeWorkspaceService
        {
            WorkspaceToReturn = new Workspace { Collections = new ObservableCollection<RequestCollection> { new() { Name = "Orders", Requests = { saved } } } }
        }, historyStore: store);
        return (vm, store, saved);
    }

    private static HistoryEntry Entry(string name, DateTimeOffset sent, Guid? source = null, int? status = 200) => new()
    {
        SourceRequestId = source, SentAtUtc = sent, StatusCode = status, IsSuccess = status is not null,
        RequestSnapshot = new RequestItem { Name = name, Url = "https://{{host}}/" + name }
    };

    [Fact]
    public void GroupsCountEntriesAndFilterTheGrid()
    {
        var (vm, _, _) = Arrange();
        vm.History.Record(Entry("older", Local(10, 12)));
        vm.History.Record(Entry("yesterday", Local(17, 12)));
        vm.History.Record(Entry("today", Local(18, 8)));
        var window = new HistoryWindowViewModel(vm, () => Now);

        Assert.Equal(new[] { 3, 1, 1, 0, 1 }, window.Groups.Select(group => group.Count));
        Assert.Equal(3, window.Rows.Count);
        window.SelectedGroup = window.Groups.Single(group => group.Group == HistoryDateGroup.Yesterday);
        Assert.Equal("yesterday", Assert.Single(window.Rows).Entry.RequestSnapshot.Name);

        window.SelectedGroup = window.Groups[0];
        window.SearchText = "OLD";
        Assert.Equal("older", Assert.Single(window.Rows).Entry.RequestSnapshot.Name);
        Assert.Equal("3 entries", window.EntryCountText);
        Assert.Equal("Keeps the newest 500 sends", window.LimitText);
    }

    [Fact]
    public void CollectionName_ComesFromTheSourceRequest_AndADeletedSourceShowsUnsaved()
    {
        var (vm, _, saved) = Arrange();
        vm.History.Record(Entry("from saved", Local(18, 8), saved.Id));
        vm.History.Record(Entry("draft", Local(18, 9)));
        var window = new HistoryWindowViewModel(vm, () => Now);
        Assert.Equal("Orders", window.Rows.Single(row => row.Entry.SourceRequestId == saved.Id).CollectionName);
        Assert.True(window.Rows.Single(row => row.Entry.SourceRequestId is null).IsUnsaved);

        vm.CollectionsPanel.Collections[0].Requests.Clear();
        window.Rebuild();
        var orphan = window.Rows.Single(row => row.Entry.SourceRequestId == saved.Id);
        Assert.Equal("Unsaved request", orphan.CollectionName);
        Assert.True(orphan.IsUnsaved);
    }

    [Fact]
    public void Compare_PicksTwoEntriesThenOpensTheComparison()
    {
        var (vm, _, _) = Arrange();
        vm.History.Record(Entry("first", Local(18, 8)));
        vm.History.Record(Entry("second", Local(18, 9)));
        var window = new HistoryWindowViewModel(vm, () => Now);
        var shown = new List<ResponseComparisonViewModel>();
        window.ShowComparison = shown.Add;

        window.SelectedRow = window.Rows[0];
        window.CompareCommand.Execute(null);
        Assert.True(window.IsPickingCompare);
        window.SelectedRow = window.Rows[0];
        Assert.Empty(shown);

        window.SelectedRow = window.Rows[1];
        Assert.Single(shown);
        Assert.False(window.IsPickingCompare);
    }

    [Fact]
    public void Compare_CanBeCancelled()
    {
        var (vm, _, _) = Arrange();
        vm.History.Record(Entry("first", Local(18, 8)));
        vm.History.Record(Entry("second", Local(18, 9)));
        var window = new HistoryWindowViewModel(vm, () => Now);
        var shown = new List<ResponseComparisonViewModel>();
        window.ShowComparison = shown.Add;

        window.SelectedRow = window.Rows[0];
        window.CompareCommand.Execute(null);
        window.CancelCompareCommand.Execute(null);
        Assert.False(window.IsPickingCompare);
        Assert.False(vm.HasPendingCompare);
        window.SelectedRow = window.Rows[1];
        Assert.Empty(shown);
    }

    [Fact]
    public async Task ClearHistory_AsksFirstAndEmptiesEveryList()
    {
        var (vm, store, _) = Arrange();
        vm.History.Record(Entry("one", Local(18, 8)));
        var window = new HistoryWindowViewModel(vm, () => Now);

        window.ConfirmClear = () => Task.FromResult(false);
        await window.ClearHistoryCommand.ExecuteAsync(null);
        Assert.Single(store.Entries);

        window.ConfirmClear = () => Task.FromResult(true);
        await window.ClearHistoryCommand.ExecuteAsync(null);
        Assert.Empty(store.Entries);
        Assert.Empty(vm.History.Entries);
        Assert.Empty(window.Rows);
        Assert.False(window.ClearHistoryCommand.CanExecute(null));
    }
}
