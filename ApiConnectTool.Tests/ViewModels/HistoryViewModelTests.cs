using System;
using System.Collections.Generic;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class HistoryViewModelTests
{
    private static HistoryEntry MakeEntry(string method, string name, string url, int? statusCode, DateTimeOffset sentAtUtc) => new()
    {
        RequestSnapshot = new RequestItem { Name = name, Url = url, Method = new System.Net.Http.HttpMethod(method) },
        StatusCode = statusCode,
        SentAtUtc = sentAtUtc
    };

    [Fact]
    public void Constructor_NoStore_StartsEmpty()
    {
        var vm = new HistoryViewModel(null);

        Assert.Empty(vm.Entries);
    }

    [Fact]
    public void Constructor_LoadsFromStoreNewestFirst()
    {
        var store = new FakeHistoryStore();
        store.Entries.Add(MakeEntry("GET", "Old", "https://a.test", 200, DateTimeOffset.UtcNow.AddMinutes(-5)));
        store.Entries.Add(MakeEntry("GET", "New", "https://b.test", 200, DateTimeOffset.UtcNow));

        var vm = new HistoryViewModel(store);

        Assert.Equal(2, vm.Entries.Count);
        Assert.Equal("Old", vm.Entries[0].RequestSnapshot.Name);
    }

    [Fact]
    public void SearchText_FiltersByNameOrUrl()
    {
        var store = new FakeHistoryStore();
        store.Entries.Add(MakeEntry("GET", "List pets", "https://api.test/pets", 200, DateTimeOffset.UtcNow));
        store.Entries.Add(MakeEntry("GET", "Get user", "https://api.test/users", 200, DateTimeOffset.UtcNow));
        var vm = new HistoryViewModel(store);

        vm.SearchText = "pets";

        Assert.Single(vm.Entries);
        Assert.Equal("List pets", vm.Entries[0].RequestSnapshot.Name);
    }

    [Fact]
    public void MethodFilter_FiltersByRequestMethod()
    {
        var store = new FakeHistoryStore();
        store.Entries.Add(MakeEntry("GET", "Get", "https://api.test/a", 200, DateTimeOffset.UtcNow));
        store.Entries.Add(MakeEntry("POST", "Post", "https://api.test/b", 201, DateTimeOffset.UtcNow));
        var vm = new HistoryViewModel(store);

        vm.MethodFilter = "POST";

        Assert.Single(vm.Entries);
        Assert.Equal("Post", vm.Entries[0].RequestSnapshot.Name);
    }

    [Fact]
    public void StatusCodeFilter_FiltersByExactStatus()
    {
        var store = new FakeHistoryStore();
        store.Entries.Add(MakeEntry("GET", "Ok", "https://api.test/a", 200, DateTimeOffset.UtcNow));
        store.Entries.Add(MakeEntry("GET", "NotFound", "https://api.test/b", 404, DateTimeOffset.UtcNow));
        var vm = new HistoryViewModel(store);

        vm.StatusCodeFilter = 404;

        Assert.Single(vm.Entries);
        Assert.Equal("NotFound", vm.Entries[0].RequestSnapshot.Name);
    }

    [Fact]
    public void DateRangeFilter_ExcludesEntriesOutsideRange()
    {
        var store = new FakeHistoryStore();
        var now = DateTimeOffset.UtcNow;
        store.Entries.Add(MakeEntry("GET", "TooOld", "https://api.test/a", 200, now.AddDays(-2)));
        store.Entries.Add(MakeEntry("GET", "InRange", "https://api.test/b", 200, now));
        var vm = new HistoryViewModel(store);

        vm.SentAfter = now.AddDays(-1);

        Assert.Single(vm.Entries);
        Assert.Equal("InRange", vm.Entries[0].RequestSnapshot.Name);
    }

    [Fact]
    public void ResendCommand_WithEntry_InvokesOnResendCallback()
    {
        var store = new FakeHistoryStore();
        var entry = MakeEntry("GET", "Get pets", "https://api.test/pets", 200, DateTimeOffset.UtcNow);
        store.Entries.Add(entry);
        HistoryEntry? resent = null;
        var vm = new HistoryViewModel(store, e => resent = e);

        vm.ResendCommand.Execute(vm.Entries[0]);

        Assert.Same(entry, resent);
    }

    [Fact]
    public void ResendCommand_NullEntry_DoesNotInvokeCallback()
    {
        var invoked = false;
        var vm = new HistoryViewModel(null, _ => invoked = true);

        vm.ResendCommand.Execute(null);

        Assert.False(invoked);
    }

    [Fact]
    public void Refresh_PicksUpNewlyRecordedEntries()
    {
        var store = new FakeHistoryStore();
        var vm = new HistoryViewModel(store);
        Assert.Empty(vm.Entries);

        store.Entries.Add(MakeEntry("GET", "New", "https://api.test/a", 200, DateTimeOffset.UtcNow));
        vm.Refresh();

        Assert.Single(vm.Entries);
    }

    private sealed class FakeHistoryStore : IHistoryStore
    {
        public List<HistoryEntry> Entries { get; } = new();

        public void Add(HistoryEntry entry) => Entries.Insert(0, entry);

        public IReadOnlyList<HistoryEntry> GetAll() => Entries;
    }
}
