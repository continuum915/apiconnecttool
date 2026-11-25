using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.ViewModels;

/// <summary>One entry of the environment editor's Scope selector: a collection, or the global
/// environments when <see cref="Collection"/> is null.</summary>
public sealed record EnvironmentScopeChoice(RequestCollection? Collection, string Name);

/// <summary>CRUD over one scope's environments and their variables: the global environments
/// (with globals), or one collection's environments (with its collection variables) when
/// <see cref="Collection"/> is set. Operates on the live collections so edits are visible to the
/// rest of the app immediately; <see cref="_onChanged"/> is invoked after each structural change
/// so the owner can persist and re-validate its own selection. With a workspace attached
/// (<see cref="UseWorkspaceScopes"/>) the editor can switch between scopes.</summary>
public partial class EnvironmentEditorViewModel : ObservableObject, IDisposable
{
    private readonly Action _onChanged;
    private Workspace? _workspace;
    private Func<RequestEnvironment?>? _globalActive;
    private Action<RequestEnvironment?>? _setGlobalActive;
    private bool _switchingScope;

    public ObservableCollection<RequestEnvironment> Environments { get; private set; }
    public ObservableCollection<RequestEnvironment> DisplayEnvironments { get; } = new();

    /// <summary>The Globals entry of the global scope; null in a collection scope.</summary>
    public RequestEnvironment? Globals { get; private set; }

    /// <summary>The "Collection variables" entry of a collection scope; null in the global scope.
    /// It edits <see cref="RequestCollection.Variables"/>, the same data as Collection Settings.</summary>
    public RequestEnvironment? CollectionVariables { get; private set; }

    /// <summary>Globals or Collection variables: listed first, never renamed, deleted or made active.</summary>
    public RequestEnvironment? ScopeVariables => Globals ?? CollectionVariables;

    [ObservableProperty] private string _searchText = string.Empty;
    public bool IsSelectedOutsideFilter => SelectedEnvironment is not null && !DisplayEnvironments.Contains(SelectedEnvironment);
    public bool IsScopeEmpty => ScopeVariables is null && Environments.Count == 0;
    public bool HasNoMatches => !IsScopeEmpty && DisplayEnvironments.Count == 0;
    partial void OnSearchTextChanged(string value) => RefreshDisplay();
    public bool CanEditEnvironment => SelectedEnvironment is not null && !IsScopeVariables(SelectedEnvironment);
    public bool IsScopeVariablesSelected => SelectedEnvironment is not null && IsScopeVariables(SelectedEnvironment);

    public bool IsScopeVariables(RequestEnvironment? environment) =>
        environment is not null && (ReferenceEquals(environment, Globals) || ReferenceEquals(environment, CollectionVariables));

    /// <summary>The collection whose environments this editor shows, or null for the global scope.</summary>
    public RequestCollection? Collection { get; private set; }
    public bool IsGlobalScope => Collection is null;

    /// <summary>The scope's display name: the collection's name, or "Global environments".</summary>
    public string ScopeName => Collection?.Name ?? AppLocalization.Get("Str.EnvironmentEditor.GlobalScope");
    public string Title => string.Format(CultureInfo.CurrentCulture, AppLocalization.Get("Str.EnvironmentEditor.WindowTitle"), ScopeName);

    /// <summary>The one-line explanation shown instead of the name and production box for
    /// Globals and Collection variables.</summary>
    public string ScopeVariablesNote => AppLocalization.Get(IsGlobalScope ? "Str.EnvironmentEditor.GlobalsNote" : "Str.EnvironmentEditor.CollectionVariablesNote");

    /// <summary>The collections a global environment can be copied or moved into.</summary>
    public IReadOnlyList<RequestCollection> TransferTargets { get; private set; } = [];
    public bool CanTransferToCollection => IsGlobalScope && CanEditEnvironment && TransferTargets.Count > 0;
    [ObservableProperty] private VariableEditorViewModel? _variableEditor;

    /// <summary>Scope selector entries: each collection, then the global environments. Empty
    /// until a workspace is attached.</summary>
    public ObservableCollection<EnvironmentScopeChoice> Scopes { get; } = new();
    [ObservableProperty] private EnvironmentScopeChoice? _selectedScope;
    public bool HasScopes => Scopes.Count > 0;

    /// <summary>The scope's active environment: the collection's, or the active global one.</summary>
    public RequestEnvironment? ActiveEnvironment => Collection is { } collection
        ? collection.ActiveEnvironment
        : _globalActive?.Invoke();

    public string ActiveSummary => string.Format(CultureInfo.CurrentCulture, AppLocalization.Get("Str.EnvironmentEditor.ActiveFor"),
        ScopeName, ActiveEnvironment?.Name ?? AppLocalization.Get("Str.Runner.NoCollectionEnvironment"));

    public EnvironmentEditorViewModel(Workspace workspace, Action onChanged) : this(workspace.Environments, onChanged)
    {
        _workspace = workspace;
        Globals = CreateGlobals(workspace);
        TransferTargets = workspace.Collections;
        SelectedEnvironment = Globals;
        RefreshDisplay();
    }

    private static RequestEnvironment CreateGlobals(Workspace workspace)
    {
        var globalsName = AppLocalization.Get("Str.Variables.Globals");
        if (globalsName == "Str.Variables.Globals") globalsName = ApiConnectTool.Services.Localization.DefaultStrings.Get("Str.Variables.Globals");
        return new() { Name = globalsName, Variables = workspace.GlobalVariables };
    }

    private static RequestEnvironment CreateCollectionVariables(RequestCollection collection)
    {
        var name = AppLocalization.Get("Str.EnvironmentEditor.CollectionVariables");
        if (name == "Str.EnvironmentEditor.CollectionVariables") name = ApiConnectTool.Services.Localization.DefaultStrings.Get("Str.EnvironmentEditor.CollectionVariables");
        return new() { Name = name, Variables = collection.Variables };
    }

    partial void OnSelectedEnvironmentChanged(RequestEnvironment? value)
    {
        VariableEditor?.Dispose();
        VariableEditor = value is null ? null : new(value.Variables, _onChanged, () => value.Name);
        OnPropertyChanged(nameof(CanEditEnvironment));
        OnPropertyChanged(nameof(IsScopeVariablesSelected));
        OnPropertyChanged(nameof(CanTransferToCollection));
        OnPropertyChanged(nameof(IsSelectedOutsideFilter));
        SetActiveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>An editor over <paramref name="collection"/>'s environments, with its active one selected.</summary>
    public static EnvironmentEditorViewModel ForCollection(RequestCollection collection, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(collection);
        var editor = new EnvironmentEditorViewModel(collection.Environments, onChanged);
        editor.EnterCollectionScope(collection);
        return editor;
    }

    private void EnterCollectionScope(RequestCollection collection)
    {
        Collection = collection;
        Globals = null;
        CollectionVariables = CreateCollectionVariables(collection);
        TransferTargets = [];
        RefreshDisplay();
        SelectedEnvironment = collection.ActiveEnvironment ?? collection.Environments.FirstOrDefault() ?? CollectionVariables;
    }

    /// <summary>Attaches the workspace so the Scope selector can switch between each collection and
    /// the global environments, and so Set as Active works for the global scope.</summary>
    public void UseWorkspaceScopes(Workspace workspace, Func<RequestEnvironment?> globalActive, Action<RequestEnvironment?> setGlobalActive)
    {
        _workspace = workspace;
        _globalActive = globalActive;
        _setGlobalActive = setGlobalActive;
        _switchingScope = true;
        try
        {
            Scopes.Clear();
            foreach (var collection in workspace.Collections) Scopes.Add(new(collection, collection.Name));
            Scopes.Add(new(null, AppLocalization.Get("Str.EnvironmentEditor.GlobalScope")));
            SelectedScope = Scopes.FirstOrDefault(scope => scope.Collection == Collection);
        }
        finally { _switchingScope = false; }
        OnPropertyChanged(nameof(HasScopes));
        NotifyScopeChanged();
    }

    partial void OnSelectedScopeChanged(EnvironmentScopeChoice? value)
    {
        if (_switchingScope || value is null || _workspace is null || value.Collection == Collection) return;
        SwitchTo(value.Collection);
    }

    private void SwitchTo(RequestCollection? collection)
    {
        Environments.CollectionChanged -= OnEnvironmentsCollectionChanged;
        Environments = collection?.Environments ?? _workspace!.Environments;
        Environments.CollectionChanged += OnEnvironmentsCollectionChanged;
        SearchText = string.Empty;
        if (collection is null)
        {
            Collection = null;
            CollectionVariables = null;
            Globals = CreateGlobals(_workspace!);
            TransferTargets = _workspace!.Collections;
            RefreshDisplay();
            SelectedEnvironment = _globalActive?.Invoke() is { } active && Environments.Contains(active) ? active : Globals;
        }
        else
        {
            EnterCollectionScope(collection);
        }
        OnPropertyChanged(nameof(Environments));
        NotifyScopeChanged();
    }

    private void NotifyScopeChanged()
    {
        foreach (var name in new[] { nameof(Collection), nameof(IsGlobalScope), nameof(ScopeName), nameof(Title), nameof(Globals),
                     nameof(CollectionVariables), nameof(ScopeVariables), nameof(ScopeVariablesNote), nameof(TransferTargets),
                     nameof(CanTransferToCollection), nameof(CanEditEnvironment), nameof(IsScopeVariablesSelected),
                     nameof(ActiveEnvironment), nameof(ActiveSummary) })
            OnPropertyChanged(name);
        SetActiveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>True for the first environment of the list, which the view heads with a label.</summary>
    public bool IsFirstListedEnvironment(RequestEnvironment? environment) =>
        environment is not null && ReferenceEquals(DisplayEnvironments.FirstOrDefault(item => !IsScopeVariables(item)), environment);

    [ObservableProperty] private RequestEnvironment? _selectedEnvironment;
    [ObservableProperty] private bool _isSecretVisible;

    public EnvironmentEditorViewModel() : this(new ObservableCollection<RequestEnvironment>(), () => { })
    {
    }

    public EnvironmentEditorViewModel(ObservableCollection<RequestEnvironment> environments, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(environments);
        ArgumentNullException.ThrowIfNull(onChanged);
        Environments = environments;
        _onChanged = onChanged;
        foreach (var environment in environments) DisplayEnvironments.Add(environment);
        environments.CollectionChanged += OnEnvironmentsCollectionChanged;
        SelectedEnvironment = Environments.FirstOrDefault();
    }

    private void OnEnvironmentsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
        => RefreshDisplay();

    public void RefreshDisplay()
    {
        DisplayEnvironments.Clear();
        if (ScopeVariables is { } scopeVariables && scopeVariables.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            DisplayEnvironments.Add(scopeVariables);
        foreach (var environment in Environments)
            if (environment.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) DisplayEnvironments.Add(environment);
        OnPropertyChanged(nameof(IsSelectedOutsideFilter));
        OnPropertyChanged(nameof(IsScopeEmpty));
        OnPropertyChanged(nameof(HasNoMatches));
        OnPropertyChanged(nameof(SelectedEnvironment));
        OnPropertyChanged(nameof(ActiveEnvironment));
        OnPropertyChanged(nameof(ActiveSummary));
    }

    public void Dispose()
    {
        Environments.CollectionChanged -= OnEnvironmentsCollectionChanged;
        VariableEditor?.Dispose();
    }

    public EnvironmentDuplicationViewModel? CreateDuplication()
    {
        if (!CanEditEnvironment || SelectedEnvironment is not { } source || !Environments.Contains(source)) return null;
        var allEnvironments = new ObservableCollection<RequestEnvironment>(Environments);
        if (Globals is not null) allEnvironments.Insert(0, Globals);
        return new(source, allEnvironments, copy =>
        {
            Environments.Add(copy);
            SearchText = string.Empty;
            SelectedEnvironment = copy;
            _onChanged();
        });
    }

    [RelayCommand]
    private void AddEnvironment()
    {
        var environment = new RequestEnvironment { Name = NextName("New Environment") };
        Environments.Add(environment);
        SearchText = string.Empty;
        SelectedEnvironment = environment;
        _onChanged();
    }

    [RelayCommand]
    private void DeleteEnvironment(RequestEnvironment? environment)
    {
        if (environment is null || IsScopeVariables(environment) || !Environments.Remove(environment))
        {
            return;
        }

        if (ReferenceEquals(SelectedEnvironment, environment))
        {
            SelectedEnvironment = Environments.FirstOrDefault() ?? ScopeVariables;
        }

        // A deleted collection environment must not stay the collection's sending target.
        if (Collection is not null && Collection.ActiveEnvironmentId == environment.Id)
        {
            Collection.ActiveEnvironmentId = null;
        }
        else if (Collection is null && ReferenceEquals(_globalActive?.Invoke(), environment))
        {
            _setGlobalActive?.Invoke(null);
        }

        _onChanged();
        OnPropertyChanged(nameof(ActiveEnvironment));
        OnPropertyChanged(nameof(ActiveSummary));
    }

    private bool CanSetActive() => CanEditEnvironment && !ReferenceEquals(SelectedEnvironment, ActiveEnvironment)
        && (Collection is not null || _setGlobalActive is not null);

    /// <summary>Makes the selected environment the scope's active one.</summary>
    [RelayCommand(CanExecute = nameof(CanSetActive))]
    private void SetActive()
    {
        if (!CanEditEnvironment || SelectedEnvironment is not { } environment) return;
        if (Collection is { } collection) collection.ActiveEnvironmentId = environment.Id;
        else _setGlobalActive?.Invoke(environment);
        _onChanged();
        OnPropertyChanged(nameof(ActiveEnvironment));
        OnPropertyChanged(nameof(ActiveSummary));
        RefreshDisplay();
        SetActiveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Starts copying (or moving) the selected global environment into a collection.</summary>
    public EnvironmentTransferViewModel? CreateTransfer(bool move) =>
        CanTransferToCollection && SelectedEnvironment is { } source && Environments.Contains(source)
            ? new EnvironmentTransferViewModel(source, TransferTargets, move)
            : null;

    /// <summary>Adds the transfer's copy to its target collection and, for a move, deletes the
    /// global original. The caller confirms a move first. Returns false, changing nothing, when
    /// the copy cannot be made.</summary>
    public bool ApplyTransfer(EnvironmentTransferViewModel transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        if (!IsGlobalScope || !Environments.Contains(transfer.Source) || transfer.TryCreateCopy() is not { } copy) return false;
        transfer.TargetCollection.Environments.Add(copy);
        if (transfer.IsMove) DeleteEnvironment(transfer.Source);
        else _onChanged();
        return true;
    }

    [RelayCommand]
    private void AddVariable()
    {
        if (SelectedEnvironment is null) return;
        SelectedEnvironment.Variables.Add(new EnvironmentVariable());
        _onChanged();
    }

    [RelayCommand]
    private void RemoveVariable(EnvironmentVariable? variable)
    {
        if (variable is null || SelectedEnvironment is null) return;
        SelectedEnvironment.Variables.Remove(variable);
        _onChanged();
    }

    [RelayCommand]
    private void ToggleSecretVisibility() => IsSecretVisible = !IsSecretVisible;

    /// <summary>Flushes any in-place edits (rename, variable value changes) that don't already
    /// trigger <see cref="_onChanged"/> on their own. Call when the editor window closes.</summary>
    public void FlushChanges() => _onChanged();

    private string NextName(string baseName)
    {
        var name = baseName;
        var suffix = 1;
        while (Environments.Any(environment => environment.Name == name))
        {
            name = $"{baseName} {suffix++}";
        }

        return name;
    }
}
