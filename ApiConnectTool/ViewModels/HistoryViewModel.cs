using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApiConnectTool.Models;
using ApiConnectTool.Services;

namespace ApiConnectTool.ViewModels;

/// <summary>
/// Backs the sidebar's History panel: a search/filtered view over <see cref="IHistoryStore"/>'s
/// entries, newest first, plus resend (opens a new tab from a captured snapshot).
/// </summary>
public partial class HistoryViewModel : ObservableObject
{
    private readonly IHistoryStore? _historyStore;
    private readonly Action<HistoryEntry>? _onResend;
    private List<HistoryEntry> _allEntries = new();
    // The UI context the view model was created on. Reading Dispatcher.UIThread from a store
    // thread instead could bind the dispatcher to that thread.
    private readonly System.Threading.SynchronizationContext? _context = System.Threading.SynchronizationContext.Current;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string? _methodFilter;
    [ObservableProperty] private int? _statusCodeFilter;
    [ObservableProperty] private DateTimeOffset? _sentAfter;
    [ObservableProperty] private DateTimeOffset? _sentBefore;
    [ObservableProperty] private string? _persistenceWarning;

    public ObservableCollection<HistoryEntry> Entries { get; } = new();

    /// <summary>Every recorded entry, newest first, ignoring the search and filters.</summary>
    public IReadOnlyList<HistoryEntry> AllEntries => _allEntries;

    /// <summary>Raised once after the entries were reloaded from the store.</summary>
    public event Action? Changed;

    public HistoryViewModel(IHistoryStore? historyStore, Action<HistoryEntry>? onResend = null)
    {
        _historyStore = historyStore;
        _onResend = onResend;
        if (historyStore is not null) historyStore.PersistenceWarningChanged += OnStoreWarningChanged;
        Refresh();
    }

    /// <summary>Called once exit is confirmed: completes when every recorded entry is on disk or
    /// its write has failed, but never waits longer than <paramref name="timeout"/>, so a stuck
    /// disk can't hold the app open.</summary>
    public async Task FlushAsync(TimeSpan timeout)
    {
        try { await (_historyStore?.FlushAsync() ?? Task.CompletedTask).WaitAsync(timeout); }
        catch (TimeoutException) { }
    }

    // The store saves on a background thread, so its warning can change off the UI thread.
    private void OnStoreWarningChanged(object? sender, EventArgs e)
    {
        if (_context is null || System.Threading.SynchronizationContext.Current == _context) ApplyStoreWarning();
        else _context.Post(_ => ApplyStoreWarning(), null);
    }

    private void ApplyStoreWarning() => PersistenceWarning = _historyStore?.PersistenceWarning is null ? null : ApiConnectTool.Services.Localization.AppLocalization.Get("Str.History.PersistenceWarning");

    /// <summary>
    /// Opens a new tab from <paramref name="entry"/>'s pre-resolution snapshot, re-resolving
    /// against whichever environment is currently selected - not whatever was selected at capture
    /// time - matching how reopening any saved request already behaves.
    /// </summary>
    [RelayCommand]
    private void Resend(HistoryEntry? entry)
    {
        if (entry is null) return;
        _onResend?.Invoke(entry);
    }

    /// <summary>Empties the history (the caller confirms first) and refreshes every list.</summary>
    public void Clear()
    {
        _historyStore?.Clear();
        Refresh();
    }

    /// <summary>Reloads from the store (newest first, as the store returns it) and reapplies the
    /// current filter. Called by MainWindowViewModel right after a send records a new entry, so
    /// the panel doesn't go stale mid-session.</summary>
    public void Refresh()
    {
        try
        {
            _allEntries = _historyStore?.GetAll().ToList() ?? [];
            ApplyStoreWarning();
        }
        catch (Exception ex) when (PersistenceFailures.IsExpected(ex))
        {
            PersistenceWarning = ApiConnectTool.Services.Localization.AppLocalization.Get("Str.History.PersistenceWarning");
        }
        ApplyFilter();
        Changed?.Invoke();
    }

    /// <summary>Adds the entry in memory and shows it at once; the store saves it in the background.</summary>
    public void Record(HistoryEntry entry)
    {
        _historyStore?.Add(entry);
        Refresh();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnMethodFilterChanged(string? value) => ApplyFilter();
    partial void OnStatusCodeFilterChanged(int? value) => ApplyFilter();
    partial void OnSentAfterChanged(DateTimeOffset? value) => ApplyFilter();
    partial void OnSentBeforeChanged(DateTimeOffset? value) => ApplyFilter();

    private void ApplyFilter()
    {
        Entries.Clear();
        foreach (var entry in _allEntries.Where(Matches))
        {
            Entries.Add(entry);
        }
    }

    private bool Matches(HistoryEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(MethodFilter) &&
            !string.Equals(entry.RequestSnapshot.Method.Method, MethodFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (StatusCodeFilter is { } statusCode && entry.StatusCode != statusCode)
        {
            return false;
        }

        if (SentAfter is { } after && entry.SentAtUtc < after)
        {
            return false;
        }

        if (SentBefore is { } before && entry.SentAtUtc > before)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var query = SearchText.Trim();
            var matchesName = entry.RequestSnapshot.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
            var matchesUrl = entry.RequestSnapshot.Url.Contains(query, StringComparison.OrdinalIgnoreCase);
            if (!matchesName && !matchesUrl)
            {
                return false;
            }
        }

        return true;
    }
}
