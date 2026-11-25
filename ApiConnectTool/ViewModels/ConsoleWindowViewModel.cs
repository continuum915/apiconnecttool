using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.ViewModels;

/// <summary>Which Console entries the window lists.</summary>
public enum ConsoleFilter { All, Network, Scripts, Warnings, Errors }

/// <summary>One Console row, with its expand state for network details.</summary>
public sealed partial class ConsoleRow(ConsoleEntry entry) : ObservableObject
{
    [ObservableProperty] private bool _isExpanded;

    public ConsoleEntry Entry { get; } = entry;
    public ConsoleEntryKind Kind => Entry.Kind;
    public string Text => Entry.Text;
    public string TimeText => Entry.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
    public bool IsNetwork => Entry.Network is not null;
    public ConsoleNetworkDetails? Network => Entry.Network;
    public string StatusText => Network?.StatusCode?.ToString(CultureInfo.CurrentCulture) ?? AppLocalization.Get("Str.Navigator.HistoryError");
    public string ElapsedText => Network is null ? string.Empty
        : string.Format(CultureInfo.CurrentCulture, AppLocalization.Get("Str.Runner.Milliseconds"), Network.Elapsed.TotalMilliseconds);
    public bool IsWarning => Kind == ConsoleEntryKind.Warning;
    public bool IsError => Kind == ConsoleEntryKind.Error;

    /// <summary>The level icon kind from the icon set.</summary>
    public string IconKind => Kind switch
    {
        ConsoleEntryKind.Network => "Network",
        ConsoleEntryKind.Script => "Script",
        ConsoleEntryKind.Warning => "Warning",
        ConsoleEntryKind.Error => "Error",
        _ => "Info"
    };

    public string IconBrushKey => Kind switch
    {
        ConsoleEntryKind.Network => "ApiConnectTool.Brush.AccentText",
        ConsoleEntryKind.Script => "ApiConnectTool.Brush.Script",
        ConsoleEntryKind.Warning => "ApiConnectTool.Brush.Warning",
        ConsoleEntryKind.Error => "ApiConnectTool.Brush.Danger",
        _ => "ApiConnectTool.Brush.Info"
    };

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = IsNetwork && !IsExpanded;
}

/// <summary>The Console window: filters with counts, search, timestamps, preserve log and a
/// status line. It only displays the session log; nothing is saved or exported.</summary>
public sealed partial class ConsoleWindowViewModel : ObservableObject, IDisposable
{
    private readonly IConsoleLog _log;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;

    [ObservableProperty] private ConsoleFilter _filter = ConsoleFilter.All;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _showTimestamps = true;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _networkCount;
    [ObservableProperty] private int _scriptCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private int _errorCount;

    public ConsoleWindowViewModel(IConsoleLog log)
    {
        _log = log;
        _log.Changed += OnLogChanged;
        Rebuild();
    }

    public ObservableCollection<ConsoleRow> Rows { get; } = new();

    public bool PreserveLog
    {
        get => _log.PreserveLog;
        set
        {
            if (_log.PreserveLog == value) return;
            _log.PreserveLog = value;
            OnPropertyChanged();
        }
    }

    public string AllText => Count("Str.Console.Filter.All", TotalCount);
    public string NetworkText => Count("Str.Console.Filter.Network", NetworkCount);
    public string ScriptsText => Count("Str.Console.Filter.Scripts", ScriptCount);
    public string WarningsText => Count("Str.Console.Filter.Warnings", WarningCount);
    public string ErrorsText => Count("Str.Console.Filter.Errors", ErrorCount);
    public bool IsEmpty => TotalCount == 0;

    public string ShownText => Rows.Count == TotalCount
        ? Count("Str.Console.Shown", TotalCount)
        : string.Format(CultureInfo.CurrentCulture, AppLocalization.Get("Str.Console.ShownOf"), Rows.Count, TotalCount);
    public string LimitsText => string.Format(CultureInfo.CurrentCulture, AppLocalization.Get("Str.Console.Limits"),
        ScriptConsoleLine.MaxLinesPerRun, ScriptConsoleLine.MaxCharactersPerRun / 1024);
    public string ErrorCountText => Count("Str.Console.ErrorCount", ErrorCount);
    public string WarningCountText => Count("Str.Console.WarningCount", WarningCount);

    partial void OnFilterChanged(ConsoleFilter value) => Rebuild();
    partial void OnSearchTextChanged(string value) => Rebuild();

    [RelayCommand]
    private void SetFilter(ConsoleFilter filter) => Filter = filter;

    [RelayCommand]
    private void Clear() => _log.Clear();

    private void OnLogChanged(object? sender, EventArgs e)
    {
        if (_context is null || SynchronizationContext.Current == _context) Rebuild();
        else _context.Post(_ => Rebuild(), null);
    }

    /// <summary>Recounts the log and relists the rows that pass the filter and search.</summary>
    public void Rebuild()
    {
        var entries = _log.Snapshot();
        TotalCount = entries.Count;
        NetworkCount = entries.Count(entry => entry.Kind == ConsoleEntryKind.Network);
        ScriptCount = entries.Count(entry => entry.Kind == ConsoleEntryKind.Script);
        WarningCount = entries.Count(entry => entry.Kind == ConsoleEntryKind.Warning);
        ErrorCount = entries.Count(entry => entry.Kind == ConsoleEntryKind.Error);

        var expanded = Rows.Where(row => row.IsExpanded).Select(row => row.Entry).ToHashSet();
        Rows.Clear();
        foreach (var entry in entries.Where(Matches))
            Rows.Add(new ConsoleRow(entry) { IsExpanded = expanded.Contains(entry) });

        foreach (var name in new[] { nameof(AllText), nameof(NetworkText), nameof(ScriptsText), nameof(WarningsText), nameof(ErrorsText),
                     nameof(IsEmpty), nameof(ShownText), nameof(ErrorCountText), nameof(WarningCountText) })
            OnPropertyChanged(name);
    }

    private bool Matches(ConsoleEntry entry)
    {
        var kindMatches = Filter switch
        {
            ConsoleFilter.Network => entry.Kind == ConsoleEntryKind.Network,
            ConsoleFilter.Scripts => entry.Kind == ConsoleEntryKind.Script,
            ConsoleFilter.Warnings => entry.Kind == ConsoleEntryKind.Warning,
            ConsoleFilter.Errors => entry.Kind == ConsoleEntryKind.Error,
            _ => true
        };
        return kindMatches && (string.IsNullOrWhiteSpace(SearchText) || entry.Text.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose() => _log.Changed -= OnLogChanged;

    private static string Count(string key, int count) => string.Format(CultureInfo.CurrentCulture, AppLocalization.Get(key), count);
}
