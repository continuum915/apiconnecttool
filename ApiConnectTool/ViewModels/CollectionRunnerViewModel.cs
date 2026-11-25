using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.ViewModels;

public partial class CollectionRunnerViewModel : ObservableObject
{
    private readonly RequestCollection _collection;
    private readonly ICollectionRunner _runner;
    private CancellationTokenSource? _cancellation;
    [ObservableProperty] private RequestEnvironment? _selectedEnvironment;
    [ObservableProperty] private EnvironmentChoice _selectedCollectionEnvironment;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _currentRequest = string.Empty;
    [ObservableProperty] private CollectionRunReport? _report;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _iterationDataFileName;
    [ObservableProperty] private string _delayMilliseconds = "0";
    [ObservableProperty] private bool _stopOnFailure;
    /// <summary>The result row whose details the details pane shows.</summary>
    [ObservableProperty] private CollectionRunEntry? _selectedEntry;
    private int _expectedTotal;
    private string? _iterationDataContent;
    private readonly CollectionFolder? _folder;
    public string Title => _folder is null ? AppLocalization.Get("Str.Runner.Title")
        : string.Format(CultureInfo.CurrentCulture, AppLocalization.Get("Str.Runner.FolderTitle"), _folder.Name);
    /// <summary>The global environments.</summary>
    public IReadOnlyList<RequestEnvironment> Environments { get; }
    /// <summary>"No environment" followed by the collection's own environments.</summary>
    public IReadOnlyList<EnvironmentChoice> CollectionEnvironments { get; }
    public ObservableCollection<CollectionRunEntry> Rows { get; } = new();

    public bool HasGlobalEnvironments => Environments.Count > 0;
    public int RunCount => Report?.RunCount ?? 0;
    public int PassedCount => Report?.PassedCount ?? 0;
    public int FailedCount => Report?.FailedCount ?? 0;
    public int ErroredCount => Report?.ErroredCount ?? 0;
    public string TotalTimeText => string.Format(CultureInfo.CurrentCulture, AppLocalization.Get("Str.Runner.Milliseconds"),
        (Report?.TotalElapsed ?? TimeSpan.Zero).TotalMilliseconds);

    /// <summary>Relative widths of the segmented progress bar: passed, failed, errored, then the
    /// requests still to run.</summary>
    public double PassedWeight => PassedCount;
    public double FailedWeight => FailedCount;
    public double ErroredWeight => ErroredCount;
    public double RemainingWeight => Math.Max(0, _expectedTotal - RunCount);

    /// <summary>The requests this run can include, in run order, all included by default. The
    /// choice belongs to this window and is not saved.</summary>
    public IReadOnlyList<RunRequestChoice> RunRequests { get; }
    public int IncludedCount => RunRequests.Count(choice => choice.IsIncluded);
    public string StartText => string.Format(CultureInfo.CurrentCulture, AppLocalization.Get("Str.Runner.StartCount"), IncludedCount);

    /// <summary>The IDs to run, or null when every request is included (a plain full run).</summary>
    public IReadOnlySet<Guid>? IncludedRequestIds => IncludedCount == RunRequests.Count
        ? null
        : RunRequests.Where(choice => choice.IsIncluded).Select(choice => choice.Request.Id).ToHashSet();
    public string Summary => Report is null ? AppLocalization.Get("Str.Runner.Ready") : string.Format(CultureInfo.CurrentCulture,
        AppLocalization.Get("Str.Runner.Summary"), Report.RunCount, Report.PassedCount, Report.FailedCount,
        Report.ErroredCount, Report.TotalElapsed.TotalMilliseconds,
        AppLocalization.Get(Report.EndState switch {
            CollectionRunEndState.Cancelled => "Str.Runner.Cancelled",
            CollectionRunEndState.StoppedOnFailure => "Str.Runner.StoppedOnFailure",
            _ => "Str.Runner.Completed"
        }));

    public CollectionRunnerViewModel(RequestCollection collection, ICollectionRunner runner,
        IReadOnlyList<RequestEnvironment> environments, RequestEnvironment? selectedEnvironment, CollectionFolder? folder = null)
    {
        _collection = collection;
        _runner = runner;
        _folder = folder;
        Environments = environments;
        SelectedEnvironment = selectedEnvironment;
        CollectionEnvironments = EnvironmentChoice.ForCollection(collection);
        _selectedCollectionEnvironment = CollectionEnvironments.FirstOrDefault(choice =>
            choice.Environment is not null && choice.Environment == collection.ActiveEnvironment) ?? CollectionEnvironments[0];
        RunRequests = CollectionRunner.RunOrder(collection, folder?.Id)
            .Select(request => new RunRequestChoice(request, collection.Folders.FirstOrDefault(item => item.Id == request.FolderId)?.Name))
            .ToArray();
        foreach (var choice in RunRequests) choice.PropertyChanged += (_, _) => OnSelectionChanged();
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(IncludedCount));
        OnPropertyChanged(nameof(StartText));
        StartCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var choice in RunRequests) choice.IsIncluded = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var choice in RunRequests) choice.IsIncluded = false;
    }

    private bool CanStart() => !IsRunning && IncludedCount > 0;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (IsRunning) return;
        ErrorMessage = null;
        IReadOnlyList<IReadOnlyDictionary<string, string>>? rows = null;
        try
        {
            if (_iterationDataContent is not null)
                rows = IterationDataParser.Parse(System.IO.Path.GetExtension(IterationDataFileName ?? ""), _iterationDataContent);
        }
        catch (FormatException error) { ErrorMessage = error.Message; return; }
        if (!int.TryParse(DelayMilliseconds, NumberStyles.None, CultureInfo.InvariantCulture, out var delay) || delay < 0)
        {
            ErrorMessage = AppLocalization.Get("Str.Runner.InvalidDelay");
            return;
        }
        Rows.Clear();
        SelectedEntry = null;
        _expectedTotal = IncludedCount * (rows?.Count ?? 1);
        Report = null;
        CurrentRequest = string.Empty;
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        IsRunning = true;
        var context = SynchronizationContext.Current;
        // Await delivery so progress and the final report cannot race queued callbacks or a later run.
        var progress = new RunProgress(value =>
        {
            void Apply()
            {
                CurrentRequest = value.CurrentRequest;
                Rows.Add(value.Entry);
                Report = new(Rows.ToArray(), CollectionRunEndState.Completed, TimeSpan.FromTicks(SumElapsedTicks()));
            }
            if (context is null || SynchronizationContext.Current == context) Apply();
            else context.Send(_ => Apply(), null);
        });
        try
        {
            Report = await _runner.RunAsync(_collection,
                new() { Environment = SelectedEnvironment, CollectionEnvironment = SelectedCollectionEnvironment.Environment, IterationRows = rows, FolderId = _folder?.Id,
                    IncludedRequestIds = IncludedRequestIds,
                    DelayBetweenRequests = TimeSpan.FromMilliseconds(delay), StopOnFailure = StopOnFailure },
                progress, cancellation.Token);
        }
        finally { _cancellation = null; IsRunning = false; }
    }

    private long SumElapsedTicks()
    {
        long ticks = 0;
        foreach (var row in Rows) ticks += row.Elapsed.Ticks;
        return ticks;
    }

    [RelayCommand] private void Cancel() => _cancellation?.Cancel();
    public void SetIterationData(string fileName, string content)
    {
        IterationDataFileName = fileName;
        _iterationDataContent = content;
        ErrorMessage = null;
    }

    public void ShowDataError(string message)
    {
        IterationDataFileName = null;
        _iterationDataContent = null;
        ErrorMessage = message;
    }

    [RelayCommand] private void ClearIterationData()
    {
        IterationDataFileName = null;
        _iterationDataContent = null;
        ErrorMessage = null;
    }
    public void Close() => Cancel();
    partial void OnReportChanged(CollectionRunReport? value)
    {
        foreach (var name in new[] { nameof(Summary), nameof(RunCount), nameof(PassedCount), nameof(FailedCount), nameof(ErroredCount),
                     nameof(TotalTimeText), nameof(PassedWeight), nameof(FailedWeight), nameof(ErroredWeight), nameof(RemainingWeight) })
            OnPropertyChanged(name);
    }
    partial void OnIsRunningChanged(bool value) => StartCommand.NotifyCanExecuteChanged();

    private sealed class RunProgress(Action<CollectionRunProgress> report) : IProgress<CollectionRunProgress>
    {
        public void Report(CollectionRunProgress value) => report(value);
    }
}
