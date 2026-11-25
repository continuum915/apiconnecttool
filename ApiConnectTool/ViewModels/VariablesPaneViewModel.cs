using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.ViewModels;

/// <summary>One variable the active request uses, as the Variables pane lists it.</summary>
public sealed record VariablePaneRow(string Name, string Source, string Value, bool IsUnresolved, bool IsProductionSource);

/// <summary>The "Variables in this request" side pane: every <c>{{name}}</c> the active tab
/// references, with its effective value and the scope it comes from. It uses the variable
/// inspector's resolution (no scripts run) and never reveals secrets.</summary>
public sealed partial class VariablesPaneViewModel : ObservableObject, IDisposable
{
    private readonly MainWindowViewModel _main;
    private VariableInspectorViewModel? _inspector;
    private RequestTabViewModel? _observedTab;

    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private string _requestName = string.Empty;
    [ObservableProperty] private string _collectionName = string.Empty;
    [ObservableProperty] private string _folderName = string.Empty;
    [ObservableProperty] private string _authText = string.Empty;
    [ObservableProperty] private string _savedState = string.Empty;
    [ObservableProperty] private bool _hasTab;

    public VariablesPaneViewModel(MainWindowViewModel main) => _main = main;

    public ObservableCollection<VariablePaneRow> Rows { get; } = new();
    public bool IsEmpty => HasTab && Rows.Count == 0;

    partial void OnIsVisibleChanged(bool value) => Refresh();

    [RelayCommand]
    private void Toggle() => IsVisible = !IsVisible;

    /// <summary>Opens the variable inspector for the active tab.</summary>
    [RelayCommand]
    private void Inspect() => _main.Commands.InspectVariablesCommand.Execute(null);

    /// <summary>Rebuilds the pane for the active tab. Called on tab switches, environment
    /// switches and workspace saves; edits within the tab refresh through the inspector.</summary>
    public void Refresh()
    {
        ObserveTab(IsVisible ? _main.ActiveTab : null);
        _inspector?.Dispose();
        _inspector = null;
        if (!IsVisible || _main.ActiveTab is not { } tab)
        {
            Rows.Clear();
            HasTab = false;
            OnPropertyChanged(nameof(IsEmpty));
            return;
        }

        HasTab = true;
        _inspector = tab.InspectVariables();
        _inspector.PropertyChanged += OnInspectorChanged;
        RebuildRows();
        RebuildGeneral();
    }

    private void OnInspectorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VariableInspectorViewModel.Variables)) RebuildRows();
    }

    private void ObserveTab(RequestTabViewModel? tab)
    {
        if (_observedTab is not null) _observedTab.PropertyChanged -= OnTabPropertyChanged;
        _observedTab = tab;
        if (tab is not null) tab.PropertyChanged += OnTabPropertyChanged;
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RequestTabViewModel.Name) or nameof(RequestTabViewModel.IsDirty)
            or nameof(RequestTabViewModel.RequestId))
            RebuildGeneral();
    }

    private void RebuildRows()
    {
        Rows.Clear();
        if (_inspector is null || _main.ActiveTab is not { } tab) return;
        var collection = tab.CollectionProvider?.Invoke();
        foreach (var variable in _inspector.Variables)
        {
            var winner = variable.Winner;
            var production = winner?.ScopeKind switch
            {
                VariableScopeKind.CollectionEnvironment => collection?.ActiveEnvironment?.IsProduction == true,
                VariableScopeKind.Environment => _main.SelectedEnvironment?.IsProduction == true,
                _ => false
            };
            var value = winner is null ? Text("Str.VariablesPane.UnresolvedValue")
                : variable.IsSecret ? SecretRedactor.Placeholder
                : variable.DisplayValue;
            Rows.Add(new VariablePaneRow(variable.Name, SourceOf(winner), value, winner is null, production));
        }
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>The pane's short source label: the environment's own name, or the scope.</summary>
    public static string SourceOf(VariableDefinition? winner) => winner?.ScopeKind switch
    {
        null => Text("Str.VariablesPane.Unresolved"),
        VariableScopeKind.Run => Text("Str.VariablesPane.Run"),
        VariableScopeKind.CollectionEnvironment or VariableScopeKind.Environment => winner.ScopeName,
        VariableScopeKind.Collection => Text("Str.VariablesPane.CollectionVariables"),
        _ => Text("Str.VariablesPane.Globals")
    };

    private void RebuildGeneral()
    {
        if (_main.ActiveTab is not { } tab) return;
        var collection = tab.CollectionProvider?.Invoke();
        var request = collection?.Requests.FirstOrDefault(item => item.Id == tab.RequestId);
        var folder = request?.FolderId is { } folderId ? collection!.Folders.FirstOrDefault(item => item.Id == folderId) : null;
        RequestName = tab.Name;
        CollectionName = collection?.Name ?? Text("Str.VariablesPane.UnsavedRequest");
        FolderName = folder?.Name ?? Text("Str.VariablesPane.NoFolder");
        AuthText = tab.AuthEditor.IsInheriting
            ? string.Format(CultureInfo.CurrentCulture, Text("Str.VariablesPane.AuthInherited"), tab.AuthEditor.InheritanceLabel)
            : Text("Str.Auth.Type." + tab.Auth.Type);
        SavedState = collection is null ? Text("Str.VariablesPane.NotSaved")
            : tab.IsDirty ? Text("Str.VariablesPane.UnsavedChanges") : Text("Str.VariablesPane.Saved");
    }

    public void Dispose()
    {
        ObserveTab(null);
        _inspector?.Dispose();
        _inspector = null;
    }

    private static string Text(string key) => AppLocalization.Get(key);
}
