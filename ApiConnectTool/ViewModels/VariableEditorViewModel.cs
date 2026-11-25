using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApiConnectTool.Models;

namespace ApiConnectTool.ViewModels;

public partial class VariableEditorViewModel : ObservableObject, IDisposable
{
    private readonly Action onChanged;
    private readonly Func<string> _scopeName;
    private readonly HashSet<EnvironmentVariable> _observed = new();
    public ObservableCollection<EnvironmentVariable> Variables { get; }
    public ObservableCollection<EnvironmentVariable> DisplayVariables { get; } = new();
    [ObservableProperty] private string _searchText = string.Empty;
    public bool IsScopeEmpty => Variables.Count == 0;
    public bool HasNoMatches => !IsScopeEmpty && DisplayVariables.Count == 0;
    partial void OnSearchTextChanged(string value) => RefreshDisplay();
    private void RefreshDisplay()
    {
        // Reuse the live rows: filtering must never copy or discard their edits.
        var matches = Variables.Where(row => row.Key.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();
        for (var i = DisplayVariables.Count - 1; i >= 0; i--)
            if (!matches.Contains(DisplayVariables[i])) DisplayVariables.RemoveAt(i);
        for (var i = 0; i < matches.Count; i++)
        {
            var existingIndex = DisplayVariables.IndexOf(matches[i]);
            if (existingIndex < 0) DisplayVariables.Insert(i, matches[i]);
            else if (existingIndex != i) DisplayVariables.Move(existingIndex, i);
        }
        OnPropertyChanged(nameof(IsScopeEmpty));
        OnPropertyChanged(nameof(HasNoMatches));
    }
    public string ScopeName => _scopeName();
    public int OverrideCount => Variables.Count(x => x.UseOverride);
    public bool HasOverrides => OverrideCount > 0;
    public VariableEditorViewModel(ObservableCollection<EnvironmentVariable> variables, Action onChanged, Func<string>? scopeName = null)
    {
        Variables = variables;
        this.onChanged = onChanged;
        _scopeName = scopeName ?? (() => ApiConnectTool.Services.Localization.AppLocalization.Get("Str.Variables.Scope"));
        Variables.CollectionChanged += OnCollectionChanged;
        ObserveRows();
    }
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ObserveRows();
    private void ObserveRows()
    {
        foreach (var row in _observed) row.PropertyChanged -= OnRowChanged;
        _observed.Clear();
        foreach (var row in Variables)
            if (_observed.Add(row)) row.PropertyChanged += OnRowChanged;
        NotifyOverrides();
        RefreshDisplay();
    }
    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EnvironmentVariable.UseOverride)) NotifyOverrides();
        if (e.PropertyName == nameof(EnvironmentVariable.Key)) RefreshDisplay();
    }
    private void NotifyOverrides()
    {
        OnPropertyChanged(nameof(OverrideCount));
        OnPropertyChanged(nameof(HasOverrides));
    }
    public void Dispose()
    {
        Variables.CollectionChanged -= OnCollectionChanged;
        foreach (var row in _observed) row.PropertyChanged -= OnRowChanged;
        _observed.Clear();
    }
    [RelayCommand] private void ResetAllOverrides()
    {
        if (!HasOverrides) return;
        foreach (var row in Variables) row.UseOverride = false;
        onChanged();
    }
    [ObservableProperty] private bool _isSecretVisible;
    [RelayCommand] private void AddVariable() { SearchText = string.Empty; Variables.Add(new()); onChanged(); }
    [RelayCommand] private void RemoveVariable(EnvironmentVariable? variable)
    { if (variable is not null && Variables.Remove(variable)) onChanged(); }
    [RelayCommand] private void ToggleSecretVisibility() => IsSecretVisible = !IsSecretVisible;
    [RelayCommand] private void ResetOverride(EnvironmentVariable? variable)
    {
        if (variable is null || !variable.UseOverride) return;
        variable.UseOverride = false;
        onChanged();
    }
}
