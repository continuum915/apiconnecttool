using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.ViewModels;

/// <summary>Date groups of the History window, by the local date of the send.</summary>
public enum HistoryDateGroup { All, Today, Yesterday, EarlierThisWeek, Older }

public sealed partial class HistoryGroupItem(HistoryDateGroup group, string name) : ObservableObject
{
    [ObservableProperty] private int _count;
    public HistoryDateGroup Group { get; } = group;
    public string Name { get; } = name;
}

/// <summary>One send as the History window lists it.</summary>
public sealed record HistoryRow(HistoryEntry Entry, string TimeText, string Method, string Url, int? StatusCode,
    string StatusText, string DurationText, string SizeText, string CollectionName, bool IsUnsaved)
{
    public bool IsError => !Entry.IsSuccess || StatusCode is null;
}

/// <summary>The History window: date groups with counts, a searchable grid of sends, request and
/// response details for the selected send, and Resend, Compare, Compare with Current and Clear
/// History. It reads the same history as the navigator's History tab.</summary>
public sealed partial class HistoryWindowViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;
    private readonly Func<DateTimeOffset> _now;

    [ObservableProperty] private HistoryGroupItem? _selectedGroup;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private HistoryRow? _selectedRow;
    [ObservableProperty] private bool _isPickingCompare;

    /// <param name="now">The current time; tests pin it to check local date boundaries.</param>
    public HistoryWindowViewModel(MainWindowViewModel main, Func<DateTimeOffset>? now = null)
    {
        _main = main;
        _now = now ?? (() => DateTimeOffset.Now);
        Groups = new[]
        {
            new HistoryGroupItem(HistoryDateGroup.All, Text("Str.HistoryWindow.Group.All")),
            new HistoryGroupItem(HistoryDateGroup.Today, Text("Str.HistoryWindow.Group.Today")),
            new HistoryGroupItem(HistoryDateGroup.Yesterday, Text("Str.HistoryWindow.Group.Yesterday")),
            new HistoryGroupItem(HistoryDateGroup.EarlierThisWeek, Text("Str.HistoryWindow.Group.EarlierThisWeek")),
            new HistoryGroupItem(HistoryDateGroup.Older, Text("Str.HistoryWindow.Group.Older"))
        };
        _selectedGroup = Groups[0];
        main.History.Changed += Rebuild;
        Rebuild();
    }

    /// <summary>Set by the window to confirm Clear History. When unset, nothing is cleared.</summary>
    public Func<Task<bool>>? ConfirmClear { get; set; }

    /// <summary>Set by the window to show a response comparison.</summary>
    public Action<ResponseComparisonViewModel>? ShowComparison { get; set; }

    public IReadOnlyList<HistoryGroupItem> Groups { get; }
    public ObservableCollection<HistoryRow> Rows { get; } = new();
    public bool HasSelection => SelectedRow is not null;

    public string EntryCountText => Format("Str.HistoryWindow.EntryCount", _main.History.AllEntries.Count);
    public string ErrorCountText => Format("Str.HistoryWindow.ErrorCount", _main.History.AllEntries.Count(entry => !entry.IsSuccess || entry.StatusCode is null));
    public string LimitText => Format("Str.HistoryWindow.Limit", HistoryStore.MaxEntries);

    /// <summary>Request details for the selected send.</summary>
    public string RequestSummary => SelectedRow is not { } row ? string.Empty
        : string.Format(CultureInfo.CurrentCulture, Text("Str.HistoryWindow.RequestSummary"), row.Method, row.Url, row.CollectionName,
            row.Entry.SentAtUtc.ToLocalTime().ToString("G", CultureInfo.CurrentCulture));

    /// <summary>Response details for the selected send.</summary>
    public string ResponseSummary => SelectedRow is not { } row ? string.Empty
        : string.Format(CultureInfo.CurrentCulture, Text("Str.HistoryWindow.ResponseSummary"), row.StatusText, row.DurationText, row.SizeText,
            row.Entry.ErrorMessage ?? ContentTypeOf(row.Entry));

    public string ResponsePreview => SelectedRow?.Entry.RedactedResponseBodyPreview ?? string.Empty;

    /// <summary>Which date group <paramref name="sentAtUtc"/> falls in, by local calendar date.
    /// Weeks start on Monday.</summary>
    public static HistoryDateGroup GroupOf(DateTimeOffset sentAtUtc, DateTimeOffset now)
    {
        var today = now.LocalDateTime.Date;
        var date = sentAtUtc.ToLocalTime().LocalDateTime.Date;
        if (date >= today) return HistoryDateGroup.Today;
        if (date == today.AddDays(-1)) return HistoryDateGroup.Yesterday;
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        return date >= weekStart ? HistoryDateGroup.EarlierThisWeek : HistoryDateGroup.Older;
    }

    partial void OnSelectedGroupChanged(HistoryGroupItem? value) => Rebuild();
    partial void OnSearchTextChanged(string value) => Rebuild();

    partial void OnSelectedRowChanged(HistoryRow? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(RequestSummary));
        OnPropertyChanged(nameof(ResponseSummary));
        OnPropertyChanged(nameof(ResponsePreview));
        foreach (var command in new IRelayCommand[] { ResendCommand, CompareCommand, CompareWithCurrentCommand }) command.NotifyCanExecuteChanged();
        if (IsPickingCompare && value is not null) FinishCompare(value.Entry);
    }

    /// <summary>Rebuilds the group counts and the grid for the selected group and search.</summary>
    public void Rebuild()
    {
        var now = _now();
        var entries = _main.History.AllEntries;
        foreach (var group in Groups)
            group.Count = group.Group == HistoryDateGroup.All ? entries.Count : entries.Count(entry => GroupOf(entry.SentAtUtc, now) == group.Group);

        var selected = SelectedRow?.Entry;
        Rows.Clear();
        foreach (var entry in entries.Where(entry => Matches(entry, now)))
            Rows.Add(RowFor(entry));
        SelectedRow = selected is null ? null : Rows.FirstOrDefault(row => row.Entry == selected);
        OnPropertyChanged(nameof(EntryCountText));
        OnPropertyChanged(nameof(ErrorCountText));
        ClearHistoryCommand.NotifyCanExecuteChanged();
    }

    private bool Matches(HistoryEntry entry, DateTimeOffset now)
    {
        if (SelectedGroup is { Group: not HistoryDateGroup.All } group && GroupOf(entry.SentAtUtc, now) != group.Group) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var query = SearchText.Trim();
        return entry.RequestSnapshot.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.RequestSnapshot.Url.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The collection that holds the send's source request, or null for an unsaved or
    /// since-deleted request.</summary>
    public string? CollectionNameOf(HistoryEntry entry) => entry.SourceRequestId is { } id
        ? _main.CollectionsPanel.Collections.FirstOrDefault(collection => collection.Requests.Any(request => request.Id == id))?.Name
        : null;

    private HistoryRow RowFor(HistoryEntry entry)
    {
        var collection = CollectionNameOf(entry);
        var status = entry.StatusCode is { } code
            ? string.IsNullOrEmpty(entry.ReasonPhrase) ? code.ToString(CultureInfo.CurrentCulture) : $"{code} {entry.ReasonPhrase}"
            : Text("Str.Navigator.HistoryError");
        return new HistoryRow(entry, entry.SentAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
            entry.RequestSnapshot.Method.Method, entry.RequestSnapshot.Url, entry.StatusCode, status,
            Format("Str.Runner.Milliseconds", entry.Elapsed.TotalMilliseconds),
            entry.ResponseSize is { } size ? FormatSize(size) : Text("Str.HistoryWindow.UnknownSize"),
            collection ?? Text("Str.Status.UnsavedRequest"), collection is null);
    }

    private static string FormatSize(long bytes) => bytes < 1024
        ? string.Format(CultureInfo.CurrentCulture, "{0} B", bytes)
        : bytes < 1024 * 1024
            ? string.Format(CultureInfo.CurrentCulture, "{0:0.0} KB", bytes / 1024.0)
            : string.Format(CultureInfo.CurrentCulture, "{0:0.0} MB", bytes / 1024.0 / 1024.0);

    private static string ContentTypeOf(HistoryEntry entry) =>
        entry.RedactedResponseHeadersText.Split('\n')
            .FirstOrDefault(line => line.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))?["Content-Type:".Length..].Trim()
        ?? string.Empty;

    private bool HasSelectedRow() => SelectedRow is not null;

    [RelayCommand(CanExecute = nameof(HasSelectedRow))]
    private void Resend()
    {
        if (SelectedRow is { } row) _main.History.ResendCommand.Execute(row.Entry);
    }

    /// <summary>Starts the two-pick compare with the selected send; choosing a second send opens
    /// the comparison. Running it again, or <see cref="CancelCompareCommand"/>, cancels.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedRow))]
    private void Compare()
    {
        if (IsPickingCompare)
        {
            CancelCompare();
            return;
        }
        if (SelectedRow is not { } row) return;
        _main.ClearPendingCompare();
        _main.TryCompareHistoryEntries(row.Entry);
        IsPickingCompare = true;
    }

    private void FinishCompare(HistoryEntry second)
    {
        if (_main.TryCompareHistoryEntries(second) is not { } comparison) return;
        IsPickingCompare = false;
        ShowComparison?.Invoke(comparison);
    }

    [RelayCommand]
    private void CancelCompare()
    {
        IsPickingCompare = false;
        _main.ClearPendingCompare();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedRow))]
    private void CompareWithCurrent()
    {
        if (SelectedRow is { } row && _main.CompareHistoryEntryWithCurrentTab(row.Entry) is { } comparison)
            ShowComparison?.Invoke(comparison);
    }

    private bool HasEntries() => _main.History.AllEntries.Count > 0;

    [RelayCommand(CanExecute = nameof(HasEntries))]
    private async Task ClearHistoryAsync()
    {
        if (ConfirmClear is null || !await ConfirmClear()) return;
        _main.History.Clear();
        SelectedRow = null;
        Rebuild();
    }

    private static string Text(string key) => AppLocalization.Get(key);
    private static string Format(string key, object value) => string.Format(CultureInfo.CurrentCulture, Text(key), value);
}
