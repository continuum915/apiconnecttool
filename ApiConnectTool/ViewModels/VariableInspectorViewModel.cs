using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.ViewModels;

public partial class VariableInspectorViewModel : ObservableObject, IDisposable
{
    private readonly List<Action> _unsubscribe = [];
    // The context the inspector was created on (the UI thread in the app). Changes raised on other
    // threads are marshalled back to it. Reading Dispatcher.UIThread here instead would bind the
    // dispatcher to whichever thread touched it first, which breaks plain view model use.
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private Func<VariableInspectorViewModel>? _refresh;
    private bool _disposed;
    private int _observationVersion;
    [ObservableProperty] private bool _revealSecrets;
    public IReadOnlyList<InspectedVariableViewModel> Variables { get; private set; }
    public bool IsEmpty => Variables.Count == 0;
    public event Action<VariableScopeKind>? Navigate;

    public void SetRefresh(Func<VariableInspectorViewModel> refresh) => _refresh = refresh;
    public void Refresh() => Invalidate();

    public void NavigateTo(VariableScopeKind scope) => Navigate?.Invoke(scope);

    public void EnableNavigation()
    {
        if (Navigate is null) return;
        foreach (var variable in Variables) variable.EnableNavigation(NavigateTo);
    }

    public VariableInspectorViewModel(RequestItem request, VariableScopeMap scopes)
    {
        var names = new List<string>();
        var resolver = new VariableResolver();
        RequestVariableTemplates.Transform(request, template =>
        {
            foreach (var name in resolver.Resolve(template, new Dictionary<string, string>()).UnresolvedVariableNames)
                if (!names.Contains(name)) names.Add(name);
            return template ?? "";
        });
        Variables = names.Select(name => new InspectedVariableViewModel(name, scopes)).ToArray();
    }

    partial void OnRevealSecretsChanged(bool value)
    {
        foreach (var variable in Variables) variable.SetReveal(value);
    }

    public void Observe(INotifyPropertyChanged source)
    {
        var version = _observationVersion;
        PropertyChangedEventHandler changed = (_, _) => InvalidateObservation(version);
        source.PropertyChanged += changed;
        _unsubscribe.Add(() => source.PropertyChanged -= changed);
    }

    public void Observe(INotifyCollectionChanged source)
    {
        var version = _observationVersion;
        NotifyCollectionChangedEventHandler changed = (_, _) => InvalidateObservation(version);
        source.CollectionChanged += changed;
        _unsubscribe.Add(() => source.CollectionChanged -= changed);
    }

    private void InvalidateObservation(int version)
    {
        if (IsOtherThread) { _context!.Post(_ => InvalidateObservation(version), null); return; }
        if (version == _observationVersion) Invalidate();
    }

    private void Invalidate()
    {
        if (IsOtherThread) { _context!.Post(_ => Invalidate(), null); return; }
        if (_disposed) return;
        RevealSecrets = false;
        if (_refresh is null) return;
        _observationVersion++;
        foreach (var unsubscribe in _unsubscribe) unsubscribe();
        _unsubscribe.Clear();
        using var replacement = _refresh();
        Variables = replacement.Variables;
        EnableNavigation();
        OnPropertyChanged(nameof(Variables));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private bool IsOtherThread => _context is not null && SynchronizationContext.Current != _context;

    public void Dispose()
    {
        _disposed = true;
        _observationVersion++;
        RevealSecrets = false;
        foreach (var unsubscribe in _unsubscribe) unsubscribe();
        _unsubscribe.Clear();
        _refresh = null;
        Navigate = null;
    }
}

public sealed class InspectedVariableViewModel : ObservableObject
{
    private readonly VariableDefinition? _winner;
    private readonly bool _secret;
    private bool _reveal;
    public string Name { get; }
    public string Status => Text(_winner is null ? "Unresolved" : _winner.ValueFailedToDecrypt ? "Unavailable" : "Resolved");
    public string DisplayValue => _winner is null ? Text("Unresolved") : FormatValue(_winner.Value, _secret && !_reveal, _winner.ValueFailedToDecrypt);
    public string Source => _winner is null ? Text("NoSource") : Scope(_winner);
    public string ValueKind => _winner is null ? Text("NoSource") : Text(_winner.IsOverride ? "Override" : "Default");
    public IReadOnlyList<InspectedDefinitionViewModel> Definitions { get; }
    public bool IsUnresolved => _winner is null;
    /// <summary>The definition that wins resolution, or null when the name is unresolved.</summary>
    public VariableDefinition? Winner => _winner;
    public bool IsSecret => _secret;
    public IRelayCommand? EditSourceCommand { get; private set; }
    public IRelayCommand? EditEnvironmentCommand { get; private set; }
    public IRelayCommand? EditGlobalsCommand { get; private set; }

    public void EnableNavigation(Action<VariableScopeKind> navigate)
    {
        EditSourceCommand = new RelayCommand(() => { if (_winner is not null) navigate(_winner.ScopeKind); },
            () => _winner is not null);
        EditEnvironmentCommand = new RelayCommand(() => navigate(VariableScopeKind.Environment));
        EditGlobalsCommand = new RelayCommand(() => navigate(VariableScopeKind.Globals));
    }

    public InspectedVariableViewModel(string name, VariableScopeMap scopes)
    {
        Name = name;
        var definitions = scopes.Definitions.Where(x => x.Name == name).ToArray();
        _winner = definitions.SingleOrDefault(x => x.IsWinner);
        _secret = scopes.SecretNames.Contains(name);
        Definitions = definitions.Select(x => new InspectedDefinitionViewModel(x, _secret || x.IsSecret)).ToArray();
    }

    public void SetReveal(bool reveal)
    {
        _reveal = reveal;
        OnPropertyChanged(nameof(DisplayValue));
        foreach (var definition in Definitions) definition.SetReveal(reveal);
    }

    internal static string Text(string key) => AppLocalization.Get("Str.VariableInspector." + key);
    internal static string FormatValue(string value, bool masked, bool failedToDecrypt = false) => masked ? Text("Masked")
        : failedToDecrypt ? Text("Unavailable") : value.Length == 0 ? Text("Empty") : value;
    internal static string Scope(VariableDefinition definition) => definition.ScopeKind is VariableScopeKind.Globals or VariableScopeKind.Run
        ? Text(definition.ScopeKind.ToString()) : string.Format(Text("NamedScope"), Text(definition.ScopeKind.ToString()), definition.ScopeName);
}

public sealed class InspectedDefinitionViewModel(VariableDefinition definition, bool secret) : ObservableObject
{
    private bool _reveal;
    public string Source => InspectedVariableViewModel.Scope(definition);
    public string Status => InspectedVariableViewModel.Text(!definition.IsEnabled ? "Disabled" : definition.IsWinner ? "Winner" : "Shadowed");
    public string ValueKind => InspectedVariableViewModel.Text(definition.IsOverride ? "Override" : "Default");
    public string DefaultValue => InspectedVariableViewModel.FormatValue(definition.DefaultValue, secret && !_reveal, definition.DefaultFailedToDecrypt);
    public string OverrideValue => InspectedVariableViewModel.FormatValue(definition.OverrideValue, secret && !_reveal, definition.OverrideFailedToDecrypt);
    public void SetReveal(bool reveal)
    {
        _reveal = reveal;
        OnPropertyChanged(nameof(DefaultValue));
        OnPropertyChanged(nameof(OverrideValue));
    }
}
