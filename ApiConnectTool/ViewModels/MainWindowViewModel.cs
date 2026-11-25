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

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IWorkspaceService _workspaceService;
    private readonly Workspace _workspace;
    private readonly IRequestExecutionService? _requestExecutionService;
    private readonly IOAuthTokenService? _oauthTokenService;
    private readonly ICookieJarService? _cookieJarService;
    private readonly ISessionStore? _sessionStore;
    private readonly IProductionSendGuard _productionSendGuard;
    private bool _isApplyingWorkspaceState;
    private HistoryEntry? _pendingCompareEntry;
    private bool _hasUnpersistedWorkspaceChanges;
    private readonly Dictionary<RequestTabViewModel, Guid> _lastHistoryEntryByTab = new();

    [ObservableProperty] private RequestTabViewModel? _activeTab;
    [ObservableProperty] private bool _isVerticalLayout = true;
    [ObservableProperty] private string? _workspaceWarning;
    public string? UnsupportedWorkspaceWarning => _workspaceService.UnsupportedSchemaVersion is { } version
        ? string.Format(ApiConnectTool.Services.Localization.AppLocalization.Get("Str.Workspace.UnsupportedVersion"), version, Workspace.CurrentSchemaVersion)
        : null;
    [ObservableProperty] private string? _workspaceSaveError;
    public UndecryptableSecretsBannerViewModel UndecryptableSecretsBanner { get; }
    [ObservableProperty] private RequestEnvironment? _selectedEnvironment;
    [ObservableProperty] private ProductionWarningMode _productionWarningMode;
    [ObservableProperty] private bool _isHistoryViewActive;
    [ObservableProperty] private bool _skipSaveAsConfirmation;
    [ObservableProperty] private AppTheme _theme;

    public Action<AppTheme>? ThemeChangedHandler { get; set; }

    /// <summary>Set by the View to show a real confirmation dialog. When unset (e.g. in tests),
    /// production sends proceed unconfirmed rather than hanging forever waiting for a prompt that
    /// will never come.</summary>
    public Func<IReadOnlyList<ProductionTarget>, Task<bool>>? ProductionSendConfirmationHandler { get; set; }

    /// <summary>Set by the View to show the naming/location dialog (first save of a new request,
    /// Save Request As, and sidebar Duplicate). When unset (e.g. in tests), the proposed default
    /// name and location are accepted immediately, as if the dialog were confirmed without
    /// "don't show again".</summary>
    public Func<SaveAsPromptRequest, Task<SaveAsPromptResult?>>? SaveAsPromptHandler { get; set; }

    /// <summary>Set by the View to ask Save / Don't Save / Cancel before dirty tabs are closed
    /// (one tab via its close button, or every dirty tab on exit). When unset (e.g. in tests),
    /// the answer is Cancel, so a dirty tab is never discarded without an explicit choice.</summary>
    public Func<IReadOnlyList<RequestTabViewModel>, Task<UnsavedChangesChoice>>? UnsavedChangesPromptHandler { get; set; }

    /// <summary>Set by the View to ask whether to exit anyway when the workspace still can't be
    /// written to disk (the argument is the failure detail). Returns true to exit. When unset
    /// (e.g. in tests), exit is blocked rather than dropping changes that never reached disk.</summary>
    public Func<string, Task<bool>>? ExitWithUnsavedWorkspacePromptHandler { get; set; }

    public ObservableCollection<RequestEnvironment> Environments { get; }
    /// <summary>The environment editor for <paramref name="collection"/>'s environments, or for
    /// the global environments when it is null.</summary>
    public EnvironmentEditorViewModel CreateEnvironmentEditor(RequestCollection? collection = null)
    {
        var editor = collection is not null
            ? EnvironmentEditorViewModel.ForCollection(collection, OnEnvironmentsChanged)
            : new EnvironmentEditorViewModel(_workspace, OnEnvironmentsChanged);
        if (collection is null && SelectedEnvironment is not null) editor.SelectedEnvironment = SelectedEnvironment;
        editor.UseWorkspaceScopes(_workspace, () => SelectedEnvironment, environment => SelectedEnvironment = environment);
        return editor;
    }
    public CollectionSettingsViewModel CreateCollectionSettings(RequestCollection collection) => new(collection,
        () => SaveWorkspaceToDisk(), _oauthTokenService, _workspace, () => SelectedEnvironment);
    public FolderSettingsViewModel CreateFolderSettings(CollectionFolder folder) => new(folder,
        _workspace.Collections.First(collection => collection.Folders.Contains(folder)), () => SaveWorkspaceToDisk(), _oauthTokenService, _workspace, () => SelectedEnvironment);
    public ObservableCollection<RequestTabViewModel> OpenRequests { get; } = new();
    public bool IsCollectionsTreeVisible => !IsHistoryViewActive && !CollectionsPanel.IsSearchActive;
    public bool IsSearchResultsVisible => !IsHistoryViewActive && CollectionsPanel.IsSearchActive;
    public HistoryViewModel History { get; }
    public string? CookiePersistenceWarning => _cookieJarService?.PersistenceWarning is null ? null : ApiConnectTool.Services.Localization.AppLocalization.Get("Str.Cookies.PersistenceWarning");
    public CollectionsViewModel CollectionsPanel { get; }
    public CollectionEnvironmentPickerViewModel CollectionEnvironmentPicker { get; }
    /// <summary>Menu bar, toolbar and navigator context-menu commands.</summary>
    public AppCommandsViewModel Commands { get; }
    /// <summary>The "Variables in this request" side pane.</summary>
    public VariablesPaneViewModel VariablesPane { get; }
    /// <summary>The status bar along the bottom of the main window.</summary>
    public StatusBarViewModel StatusBar { get; }
    /// <summary>The session Console: sends, runs, script output, warnings and errors.</summary>
    public IConsoleLog ConsoleLog { get; } = new ConsoleLog();
    /// <summary>The global environment picker shows only once a global environment exists.</summary>
    public bool HasGlobalEnvironments => Environments.Count > 0;

    public MainWindowViewModel(IWorkspaceService? workspaceService = null, IRequestExecutionService? requestExecutionService = null, IOAuthTokenService? oauthTokenService = null, IHistoryStore? historyStore = null, ICookieJarService? cookieJarService = null, ISessionStore? sessionStore = null)
    {
        _workspaceService = workspaceService ?? new WorkspaceService();
        _requestExecutionService = requestExecutionService;
        _oauthTokenService = oauthTokenService;
        _cookieJarService = cookieJarService;
        _sessionStore = sessionStore;
        History = new HistoryViewModel(historyStore, ResendFromHistory);
        History.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(HistoryViewModel.PersistenceWarning)) foreach (var tab in OpenRequests) tab.RefreshHistoryWarning(); };
        _workspace = _workspaceService.LoadWorkspace();
        _workspace.Collections ??= new ObservableCollection<RequestCollection>();
        CollectionsPanel = new CollectionsViewModel(_workspace.Collections, persist: () => SaveWorkspaceToDisk(), openRequest: OpenRequest, requestsRemoved: CloseTabsOfRequests, resolveDuplicateName: ResolveDuplicateNameAsync, requestRenamed: RetitleTabsOf, workspaceId: () => _workspace.Id);
        CollectionsPanel.RequestsReplaced = ReloadReplacedTabsAsync;
        CollectionsPanel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CollectionsViewModel.IsSearchActive))
            {
                OnPropertyChanged(nameof(IsCollectionsTreeVisible));
                OnPropertyChanged(nameof(IsSearchResultsVisible));
            }
        };
        Environments = _workspace.Environments ?? new ObservableCollection<RequestEnvironment>();
        _workspace.Environments = Environments;
        Environments.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasGlobalEnvironments));
        CollectionEnvironmentPicker = new CollectionEnvironmentPickerViewModel(() => ActiveTab?.CollectionProvider?.Invoke(), () => _workspace.Collections, () => SaveWorkspaceToDisk());
        WorkspaceWarning = _workspaceService.LastLoadError;
        UndecryptableSecretsBanner = new UndecryptableSecretsBannerViewModel(_workspaceService.UndecryptableSecrets);
        _productionSendGuard = new ProductionSendGuard(() => _workspace.ProductionWarningMode, targets => ProductionSendConfirmationHandler?.Invoke(targets) ?? Task.FromResult(true));

        if (WorkspaceMigrator.Normalize(_workspace)) SaveWorkspaceToDisk();

        _isApplyingWorkspaceState = true;
        SelectedEnvironment = Environments.FirstOrDefault(environment => environment.Id == _workspace.SelectedEnvironmentId);
        ProductionWarningMode = _workspace.ProductionWarningMode;
        SkipSaveAsConfirmation = _workspace.SkipSaveAsConfirmation;
        Theme = Enum.IsDefined(_workspace.Theme) ? _workspace.Theme : AppTheme.Win7Light;
        RestoreSession();
        _isApplyingWorkspaceState = false;
        OpenRequests.CollectionChanged += (_, _) => SaveSession();
        Commands = new AppCommandsViewModel(this);
        CollectionEnvironmentPicker.ManageRequested = collection => Commands.Dialogs?.OpenEnvironments(collection);
        VariablesPane = new VariablesPaneViewModel(this) { IsVisible = _sessionStore?.Load().VariablesPaneVisible ?? true };
        VariablesPane.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(VariablesPaneViewModel.IsVisible)) SaveSession(); };
        VariablesPane.Refresh();
        StatusBar = new StatusBarViewModel(this);
    }

    public CollectionRunnerViewModel CreateCollectionRunner(RequestCollection collection, CollectionFolder? folder = null) => new(collection,
        new CollectionRunner(new RequestRunner(_requestExecutionService ?? throw new InvalidOperationException()), _productionSendGuard, _workspace, () => SaveWorkspaceToDisk(), ConsoleLog),
        Environments.ToArray(), SelectedEnvironment, folder);

    public CollectionRunnerViewModel CreateFolderRunner(CollectionFolder folder) => CreateCollectionRunner(
        _workspace.Collections.First(collection => collection.Folders.Contains(folder)), folder);

    [RelayCommand]
    private void ToggleLayout() => IsVerticalLayout = !IsVerticalLayout;

    [RelayCommand]
    private void ShowCollections() => IsHistoryViewActive = false;

    [RelayCommand]
    private void ShowHistory() => IsHistoryViewActive = true;

    [RelayCommand]
    private void AddTab() => ActivateNewTab(CreateTab(null));

    private void ActivateNewTab(RequestTabViewModel tab)
    {
        OpenRequests.Add(tab);
        ActiveTab = tab;
    }

    public void SwitchTab(int offset)
    {
        if (OpenRequests.Count == 0) return;
        var current = ActiveTab is null ? 0 : Math.Max(0, OpenRequests.IndexOf(ActiveTab));
        ActiveTab = OpenRequests[((current + offset) % OpenRequests.Count + OpenRequests.Count) % OpenRequests.Count];
    }

    partial void OnSelectedEnvironmentChanged(RequestEnvironment? value)
    {
        if (_isApplyingWorkspaceState) return;
        _workspace.SelectedEnvironmentId = value?.Id;
        SaveWorkspaceToDisk();
    }

    partial void OnProductionWarningModeChanged(ProductionWarningMode value)
    {
        if (_isApplyingWorkspaceState) return;
        _workspace.ProductionWarningMode = value;
        SaveWorkspaceToDisk();
    }

    partial void OnThemeChanged(AppTheme value)
    {
        if (_isApplyingWorkspaceState) return;
        _workspace.Theme = value;
        ThemeChangedHandler?.Invoke(value);
        SaveWorkspaceToDisk();
    }

    partial void OnSkipSaveAsConfirmationChanged(bool value)
    {
        if (_isApplyingWorkspaceState) return;
        _workspace.SkipSaveAsConfirmation = value;
        SaveWorkspaceToDisk();
    }

    partial void OnIsHistoryViewActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCollectionsTreeVisible));
        OnPropertyChanged(nameof(IsSearchResultsVisible));
    }

    /// <summary>Callback for EnvironmentEditorViewModel: persists CRUD changes and drops a
    /// dangling selection if the currently selected environment was just deleted.</summary>
    public void OnEnvironmentsChanged()
    {
        if (SelectedEnvironment is not null && !Environments.Contains(SelectedEnvironment))
        {
            SelectedEnvironment = null;
        }

        SaveWorkspaceToDisk();
    }

    private RequestTabViewModel CreateTab(RequestItem? request)
    {
        RequestTabViewModel tab = null!;
        Action<HistoryEntry> record = entry => RecordHistoryEntry(tab, entry);
        if (_requestExecutionService is null)
        {
            tab = request is null ? new RequestTabViewModel() : RequestTabViewModel.FromRequest(request);
        }
        else tab = request is null
            ? new RequestTabViewModel(_requestExecutionService, () => SelectedEnvironment, _productionSendGuard.ConfirmSendAsync, _oauthTokenService, record, _cookieJarService)
            : RequestTabViewModel.FromRequest(request, _requestExecutionService, () => SelectedEnvironment, _productionSendGuard.ConfirmSendAsync, _oauthTokenService, record, _cookieJarService);
        tab.WorkspaceProvider = () => _workspace;
        tab.Summary.GlobalEnvironmentProvider = () => SelectedEnvironment;
        tab.RunCompleted = (snapshot, run) => ConsoleRecorder.RecordSend(ConsoleLog, snapshot.Name, run,
            run.Execution.ResolvedSecretValues.Concat(run.Scripts.SelectMany(script => script.SecretValues)));
        tab.HistoryWarningProvider = () => History.PersistenceWarning;
        tab.SaveScopes = () => SaveWorkspaceToDisk();
        tab.CollectionProvider = () => _workspace.Collections.FirstOrDefault(collection => collection.Requests.Any(item => item.Id == tab.RequestId));
        tab.RefreshAuthContext();
        return tab;
    }

    private void RecordHistoryEntry(RequestTabViewModel tab, HistoryEntry entry)
    {
        _lastHistoryEntryByTab[tab] = entry.Id;
        History.Record(entry);
    }

    /// <summary>The history entry recorded by <paramref name="tab"/>'s latest send, if any.</summary>
    public Guid? LastHistoryEntryIdOf(RequestTabViewModel tab) =>
        _lastHistoryEntryByTab.TryGetValue(tab, out var id) ? id : null;

    /// <summary>
    /// Always opens a brand new tab (never reuses an already-open one, unlike OpenRequest) from
    /// the entry's pre-resolution snapshot. If that snapshot's Id matches a still-saved request,
    /// a plain Save on this tab overwrites it - the same "reopen and save" behavior any saved
    /// request already has; a never-saved entry's random Id matches nothing, so Save creates a
    /// new request instead, matching Save As.
    /// </summary>
    private void ResendFromHistory(HistoryEntry entry)
    {
        var tab = CreateTab(entry.RequestSnapshot);
        if (entry.RequestBodyWasTruncatedForHistory) tab.ShowIncompleteBodyNotice();
        ActivateNewTab(tab);
    }

    /// <summary>Compares two history entries' captured responses. Returns null if fewer than two
    /// distinct entries have been picked so far - the second call with a different entry produces
    /// the comparison and resets the picker.</summary>
    public ResponseComparisonViewModel? TryCompareHistoryEntries(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (_pendingCompareEntry is null || _pendingCompareEntry == entry)
        {
            _pendingCompareEntry = entry;
            return null;
        }

        var comparison = new ResponseComparisonViewModel(
            ResponseSnapshot.FromHistoryEntry(_pendingCompareEntry),
            ResponseSnapshot.FromHistoryEntry(entry));
        _pendingCompareEntry = null;
        return comparison;
    }

    /// <summary>Cancels an in-progress two-entry compare pick.</summary>
    public void ClearPendingCompare() => _pendingCompareEntry = null;

    public bool HasPendingCompare => _pendingCompareEntry is not null;

    /// <summary>Compares a history entry's captured response against the active tab's live one.</summary>
    public ResponseComparisonViewModel? CompareHistoryEntryWithCurrentTab(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ActiveTab is null ? null : new ResponseComparisonViewModel(
            ResponseSnapshot.FromCurrentTab(ActiveTab),
            ResponseSnapshot.FromHistoryEntry(entry));
    }

    [RelayCommand]
    private async Task CloseTabAsync(RequestTabViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        if (tab.IsDirty && !await ResolveUnsavedChangesAsync([tab]))
        {
            return;
        }

        CloseTabCore(tab);
    }

    /// <summary>
    /// Called by the View before the main window closes. Returns true when it is safe to exit:
    /// no tab is dirty, or the user chose to save (and every save succeeded) or to discard.
    /// Returns false when the user cancelled or a save failed, so the window must stay open.
    /// </summary>
    public async Task<bool> ConfirmExitAsync()
    {
        var dirtyTabs = OpenRequests.Where(tab => tab.IsDirty).ToList();
        if (dirtyTabs.Count > 0 && !await ResolveUnsavedChangesAsync(dirtyTabs))
        {
            return false;
        }

        // A sidebar/environment/settings change whose save failed only exists in memory. Retry
        // once; if the disk is still refusing, exiting would lose it, so ask first.
        if (_hasUnpersistedWorkspaceChanges && !SaveWorkspaceToDisk())
        {
            return await (ExitWithUnsavedWorkspacePromptHandler?.Invoke(WorkspaceSaveError ?? string.Empty)
                ?? Task.FromResult(false));
        }

        return true;
    }

    /// <summary>Asks the user what to do with <paramref name="dirtyTabs"/> and, for Save, saves
    /// each one. Returns true only if the tabs may now be closed without losing edits.</summary>
    private async Task<bool> ResolveUnsavedChangesAsync(IReadOnlyList<RequestTabViewModel> dirtyTabs)
    {
        var choice = await (UnsavedChangesPromptHandler?.Invoke(dirtyTabs)
            ?? Task.FromResult(UnsavedChangesChoice.Cancel));

        switch (choice)
        {
            case UnsavedChangesChoice.Discard:
                return true;
            case UnsavedChangesChoice.Save:
                // Stop at the first failed or cancelled save and leave every tab open with its
                // edits intact - closing after a failed save is exactly the data loss this prompt
                // prevents.
                foreach (var tab in dirtyTabs)
                {
                    if (!await SaveTabAsync(tab))
                    {
                        return false;
                    }
                }

                return true;
            default:
                return false;
        }
    }

    [RelayCommand]
    private void DiscardAndCloseTab(RequestTabViewModel? tab)
    {
        if (tab is not null)
        {
            CloseTabCore(tab);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (ActiveTab is null)
        {
            return;
        }

        await SaveTabAsync(ActiveTab);
    }

    [RelayCommand]
    private async Task SaveAsAsync()
    {
        if (ActiveTab is null)
        {
            return;
        }

        var tab = ActiveTab;
        var source = CollectionsPanel.Collections.SelectMany(collection => collection.Requests).FirstOrDefault(request => request.Id == tab.RequestId);
        var destinations = CollectionsPanel.GetSaveDestinations();
        var defaultDestination = (source is null ? null : CollectionsPanel.DestinationOf(source, destinations)) ?? destinations.FirstOrDefault();
        var answer = await ResolveSaveAsAsync(SaveAsPromptMode.SaveAsCopy, $"Copy of {tab.Name}", destinations, defaultDestination);
        if (answer is null)
        {
            return;
        }

        SaveTabAsNewRequest(tab, answer.Value.Name, answer.Value.Destination, asCopy: true);
    }

    /// <summary>
    /// Shows the naming (and, except for Duplicate, location) dialog and returns the chosen name
    /// and destination, or null if the user cancelled. Save As and Duplicate skip the dialog when
    /// the user previously chose "don't show again"; the first save of a new request always asks,
    /// since there is no sensible place to put it without asking.
    /// </summary>
    private async Task<(string Name, SaveDestination? Destination)?> ResolveSaveAsAsync(
        SaveAsPromptMode mode,
        string defaultName,
        IReadOnlyList<SaveDestination> destinations,
        SaveDestination? defaultDestination)
    {
        if (mode != SaveAsPromptMode.SaveNew && SkipSaveAsConfirmation)
        {
            return (defaultName, defaultDestination);
        }

        var prompt = await (SaveAsPromptHandler?.Invoke(new SaveAsPromptRequest(mode, defaultName, destinations, defaultDestination))
            ?? Task.FromResult<SaveAsPromptResult?>(new SaveAsPromptResult(defaultName, false)));
        if (prompt is null)
        {
            return null;
        }

        if (prompt.DontShowAgain && mode != SaveAsPromptMode.SaveNew)
        {
            SkipSaveAsConfirmation = true;
        }

        var name = string.IsNullOrWhiteSpace(prompt.Name) ? defaultName : prompt.Name.Trim();
        return (name, prompt.Destination ?? defaultDestination);
    }

    /// <summary>The naming prompt for a sidebar Duplicate (no location choice): returns the chosen
    /// name, or null if the user cancelled.</summary>
    private async Task<string?> ResolveDuplicateNameAsync(string defaultName) =>
        (await ResolveSaveAsAsync(SaveAsPromptMode.Duplicate, defaultName, [], null))?.Name;

    [RelayCommand]
    private void OpenRequest(RequestItem? request)
    {
        if (request is null)
        {
            return;
        }

        var existing = OpenRequests.FirstOrDefault(tab => tab.RequestId == request.Id);
        if (existing is not null)
        {
            ActiveTab = existing;
            return;
        }

        ActivateNewTab(CreateTab(request));
    }

    /// <summary>
    /// Saves the tab over its existing request, or - for a request not in any collection yet -
    /// asks for a name and location first (cancelling returns false and saves nothing). Returns
    /// whether it saved.
    /// </summary>
    private async Task<bool> SaveTabAsync(RequestTabViewModel tab)
    {
        if (CollectionsPanel.Collections.Any(collection => collection.Requests.Any(request => request.Id == tab.RequestId)))
        {
            return SaveTabOverExisting(tab);
        }

        var destinations = CollectionsPanel.GetSaveDestinations();
        var defaultName = string.IsNullOrWhiteSpace(tab.Name) ? "New Request" : tab.Name;
        var answer = await ResolveSaveAsAsync(SaveAsPromptMode.SaveNew, defaultName, destinations, destinations.FirstOrDefault());
        return answer is not null && SaveTabAsNewRequest(tab, answer.Value.Name, answer.Value.Destination);
    }

    /// <summary>
    /// Writes the tab over its saved request and persists the workspace. The tab is only marked
    /// saved once the disk write succeeds; if it fails, the previous version is put back and the
    /// tab stays dirty, so a failed save can never make unsaved edits look saved (and then be
    /// closed or exited away without a prompt).
    /// </summary>
    private bool SaveTabOverExisting(RequestTabViewModel tab)
    {
        var collection = CollectionsPanel.Collections.First(candidate => candidate.Requests.Any(request => request.Id == tab.RequestId));
        var existing = collection.Requests.First(request => request.Id == tab.RequestId);
        var snapshot = tab.CreateSnapshot(existing.Id);
        // CreateSnapshot doesn't know about folder placement - preserve it across a plain
        // (non-Save-As) save rather than silently resetting it to the collection root.
        snapshot.FolderId = existing.FolderId;
        snapshot.SortOrder = existing.SortOrder;
        var index = collection.Requests.IndexOf(existing);
        collection.Requests[index] = snapshot;
        if (!SaveWorkspaceToDisk(rollback: () => collection.Requests[index] = existing))
        {
            return false;
        }

        tab.MarkSaved(snapshot.Id);
        return true;
    }

    /// <summary>
    /// Saves the tab as a brand-new request (first save, or Save As) at the end of
    /// <paramref name="destination"/>, or of a default "Requests" collection when there are no
    /// collections to choose from. Same mark-saved-only-after-disk rule and rollback as
    /// <see cref="SaveTabOverExisting"/>.
    /// </summary>
    private bool SaveTabAsNewRequest(RequestTabViewModel tab, string desiredName, SaveDestination? destination, bool asCopy = false)
    {
        var snapshot = asCopy ? tab.CreateSnapshot().Clone() : tab.CreateSnapshot(Guid.NewGuid());
        var undoPlacement = CollectionsPanel.PlaceSavedRequest(snapshot, desiredName, destination);
        if (!SaveWorkspaceToDisk(rollback: undoPlacement))
        {
            return false;
        }

        tab.Name = snapshot.Name;
        if (asCopy) tab.Auth = snapshot.Auth.Clone();
        tab.MarkSaved(snapshot.Id);
        SaveSession();
        return true;
    }

    partial void OnActiveTabChanged(RequestTabViewModel? value)
    {
        CollectionEnvironmentPicker.Refresh();
        VariablesPane?.Refresh();
        SaveSession();
    }

    private void RestoreSession()
    {
        if (_sessionStore is null) return;
        var session = _sessionStore.Load();
        foreach (var request in session.ResolveRequests(_workspace.Collections)) OpenRequests.Add(CreateTab(request));
        ActiveTab = OpenRequests.FirstOrDefault(tab => tab.RequestId == session.ActiveRequestId) ?? OpenRequests.FirstOrDefault();
    }

    private void SaveSession()
    {
        if (!_isApplyingWorkspaceState && _sessionStore is not null && _workspaceService.UnsupportedSchemaVersion is null)
            _sessionStore.Save(SessionState.Capture(OpenRequests.Select(tab => tab.RequestId), ActiveTab?.RequestId, _workspace.Collections)
                with { VariablesPaneVisible = VariablesPane?.IsVisible ?? true });
    }

    /// <summary>Gives every open tab of a renamed request its new name, without touching the
    /// tab's dirty state or unsaved edits.</summary>
    /// <summary>After an import overwrote requests in place, reopens their tabs on the new
    /// content. A tab with unsaved edits asks first: Save keeps the edits, Don't Save reloads,
    /// Cancel leaves the tab as it is.</summary>
    private async Task ReloadReplacedTabsAsync(IReadOnlyCollection<Guid> requestIds)
    {
        foreach (var tab in OpenRequests.Where(tab => tab.RequestId is { } id && requestIds.Contains(id)).ToList())
        {
            if (tab.IsDirty)
            {
                var choice = await (UnsavedChangesPromptHandler?.Invoke([tab]) ?? Task.FromResult(UnsavedChangesChoice.Cancel));
                if (choice == UnsavedChangesChoice.Save) await SaveTabAsync(tab);
                if (choice != UnsavedChangesChoice.Discard) continue;
            }
            var request = CollectionsPanel.Collections.SelectMany(collection => collection.Requests).FirstOrDefault(item => item.Id == tab.RequestId);
            var index = OpenRequests.IndexOf(tab);
            if (request is null || index < 0) continue;
            var wasActive = ActiveTab == tab;
            var fresh = CreateTab(request);
            OpenRequests[index] = fresh;
            _lastHistoryEntryByTab.Remove(tab);
            tab.AuthEditor.Dispose();
            if (wasActive) ActiveTab = fresh;
        }
    }

    private void RetitleTabsOf(RequestItem request)
    {
        foreach (var tab in OpenRequests.Where(tab => tab.RequestId == request.Id))
        {
            tab.ApplySavedName(request.Name);
        }
    }

    private void CloseTabCore(RequestTabViewModel tab)
    {
        var index = OpenRequests.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        OpenRequests.RemoveAt(index);
        _lastHistoryEntryByTab.Remove(tab);
        tab.AuthEditor.Dispose();
        ActiveTab = OpenRequests.Count == 0 ? null : OpenRequests[Math.Min(index, OpenRequests.Count - 1)];
    }

    /// <summary>Closes, without prompting, every tab showing one of <paramref name="requestIds"/> -
    /// those requests no longer exist, so there is nothing left to save them over.</summary>
    private void CloseTabsOfRequests(IReadOnlyCollection<Guid> requestIds)
    {
        foreach (var tab in OpenRequests.Where(tab => tab.RequestId is { } id && requestIds.Contains(id)).ToList())
        {
            CloseTabCore(tab);
        }
    }

    /// <summary>
    /// Persists the workspace. A failed write never throws out of here (every sidebar,
    /// environment, and settings command funnels through this, and an unhandled exception from a
    /// command would take the app down): it surfaces the failure in <see cref="WorkspaceSaveError"/>
    /// and returns false. Without <paramref name="rollback"/>, the change stays in memory and is
    /// flagged so the next successful save - or the exit check - picks it up. With it, the
    /// caller's in-memory change is undone instead (used by tab saves, whose edits remain safe in
    /// the still-dirty tab).
    /// </summary>
    public bool SaveWorkspaceToDisk(Action? rollback = null)
    {
        foreach (var tab in OpenRequests) tab.RefreshAuthContext();
        CollectionsPanel.Refresh();
        CollectionEnvironmentPicker.Refresh();
        VariablesPane?.Refresh();
        StatusBar?.Refresh();
        try
        {
            _workspaceService.SaveWorkspace(_workspace);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            if (rollback is not null)
            {
                rollback();
                CollectionsPanel.Refresh();
            }
            else
            {
                _hasUnpersistedWorkspaceChanges = true;
            }

            // A refused write to a newer workspace is explained by its own persistent banner, not a retryable save error.
            WorkspaceSaveError = ex is UnsupportedWorkspaceVersionException ? null : ex.Message;
            OnPropertyChanged(nameof(UnsupportedWorkspaceWarning));
            return false;
        }

        _hasUnpersistedWorkspaceChanges = false;
        WorkspaceSaveError = null;
        return true;
    }

    [RelayCommand]
    private void DismissWorkspaceWarning() => WorkspaceWarning = null;

    [RelayCommand]
    private void DismissWorkspaceSaveError() => WorkspaceSaveError = null;
}
