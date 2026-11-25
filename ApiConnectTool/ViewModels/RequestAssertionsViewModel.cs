using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.ViewModels;

public partial class RequestAssertionsViewModel : ObservableObject
{
    private readonly Action _markDirty;
    private readonly HashSet<Assertion> _observed = new();
    private bool _loading;
    private bool _notEvaluated;
    public ObservableCollection<Assertion> Assertions { get; } = new();
    public ObservableCollection<AssertionResult> Results { get; } = new();
    /// <summary>Script console output of the last send, pre-request then test.</summary>
    public ObservableCollection<ScriptConsoleLine> ConsoleLines { get; } = new();
    public bool HasConsoleLines => ConsoleLines.Count > 0;
    [ObservableProperty] private string? _preRequestScript;
    [ObservableProperty] private string? _testScript;
    partial void OnPreRequestScriptChanged(string? value) { if (!_loading) _markDirty(); }
    partial void OnTestScriptChanged(string? value) { if (!_loading) _markDirty(); }

    [ObservableProperty] private Assertion? _selectedAssertion;
    public int PassedCount => Results.Count(result => result.Passed);
    public bool HasResults => Results.Count > 0;
    public string Summary => _notEvaluated ? AppLocalization.Get("Str.Assertions.NotEvaluated")
        : !HasResults ? AppLocalization.Get("Str.Assertions.Empty")
        : AssertionResult.Summarize(Results);

    public RequestAssertionsViewModel(Action markDirty)
    {
        _markDirty = markDirty;
        Assertions.CollectionChanged += (_, _) =>
        {
            foreach (var item in _observed.Where(item => !Assertions.Contains(item)).ToArray())
            {
                item.PropertyChanged -= OnAssertionChanged;
                _observed.Remove(item);
            }
            foreach (var item in Assertions)
                if (_observed.Add(item)) item.PropertyChanged += OnAssertionChanged;
            if (!_loading) _markDirty();
        };
    }

    public void Load(IEnumerable<Assertion> assertions, string? preRequestScript = null, string? testScript = null)
    {
        _loading = true;
        try
        {
            PreRequestScript = preRequestScript;
            TestScript = testScript;
            Assertions.Clear();
            foreach (var assertion in assertions) Assertions.Add(assertion.Clone());
        }
        finally { _loading = false; }
    }

    public ObservableCollection<Assertion> CreateSnapshot() => new(Assertions.Select(assertion => assertion.Clone()));

    public void Evaluate(IEnumerable<Assertion> assertions, RequestExecutionResult response)
    {
        Results.Clear();
        ConsoleLines.Clear();
        _notEvaluated = !response.IsSuccess || response.StatusCode is null;
        foreach (var result in new AssertionEngine().Evaluate(assertions, response)) Results.Add(result);
        NotifyResults();
    }

    public void Apply(RequestRunResult run)
    {
        Results.Clear();
        ConsoleLines.Clear();
        _notEvaluated = !run.TestsEvaluated;
        foreach (var result in run.Tests) Results.Add(result);
        foreach (var line in run.Console) ConsoleLines.Add(line);
        NotifyResults();
    }

    public void NotEvaluated()
    {
        Results.Clear();
        ConsoleLines.Clear();
        _notEvaluated = true;
        NotifyResults();
    }

    private void NotifyResults()
    {
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasConsoleLines));
        OnPropertyChanged(nameof(PassedCount));
        OnPropertyChanged(nameof(Summary));
    }

    private void OnAssertionChanged(object? sender, PropertyChangedEventArgs args) => _markDirty();
    [RelayCommand] private void AddAssertion() => Assertions.Add(new Assertion());
    [RelayCommand] private void RemoveAssertion()
    {
        if (SelectedAssertion is { } selected) Assertions.Remove(selected);
    }
}
