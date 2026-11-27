using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApiConnectTool.Models;

namespace ApiConnectTool.ViewModels;

/// <summary>Windows and pickers the menu and toolbar open. The main window implements it; tests
/// substitute a recorder. View models decide the target, the view only shows the window.</summary>
public interface IAppDialogs
{
    void OpenCollectionRunner(RequestCollection collection, CollectionFolder? folder);
    void OpenCollectionSettings(RequestCollection collection);
    void OpenEnvironments(RequestCollection? collection);
    Task ExportCollectionAsync(RequestCollection collection, bool asPostman);
    Task CopyAsCurlAsync(RequestTabViewModel tab, bool revealSecrets);
    void InspectVariables(RequestTabViewModel tab);
    void ShowComparison(ResponseComparisonViewModel comparison);
    void OpenHelp(string? topicId);
    void OpenHistoryWindow();
    void OpenConsole();
}

/// <summary>The command surface shared by the menu bar, the toolbar and the navigator context
/// menus. Collection commands act on the selected navigator node's collection; request commands
/// act on the active tab. Each command disables when it has no target.</summary>
public sealed partial class AppCommandsViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;
    private RequestTabViewModel? _observedTab;

    public AppCommandsViewModel(MainWindowViewModel main)
    {
        _main = main;
        main.PropertyChanged += OnMainPropertyChanged;
        main.CollectionsPanel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CollectionsViewModel.SelectedNode)) Refresh();
        };
        main.History.Changed += Refresh;
        ObserveTab(main.ActiveTab);
    }

    /// <summary>Set by the main window. When unset (tests without a recorder), window-opening
    /// commands do nothing.</summary>
    public IAppDialogs? Dialogs { get; set; }

    public RequestCollection? SelectedCollection => _main.CollectionsPanel.SelectedNode switch
    {
        CollectionTreeNode node => node.Collection,
        FolderTreeNode node => _main.CollectionsPanel.Collections.FirstOrDefault(collection => collection.Folders.Contains(node.Folder)),
        RequestItem request => _main.CollectionsPanel.Collections.FirstOrDefault(collection => collection.Requests.Contains(request)),
        _ => null
    };

    public CollectionFolder? SelectedFolder => (_main.CollectionsPanel.SelectedNode as FolderTreeNode)?.Folder;

    private RequestTabViewModel? Tab => _main.ActiveTab;

    /// <summary>The saved request behind the active tab, or null for a draft.</summary>
    public RequestItem? ActiveRequest => Tab is { } tab
        ? _main.CollectionsPanel.Collections.SelectMany(collection => collection.Requests).FirstOrDefault(request => request.Id == tab.RequestId)
        : null;

    /// <summary>Run Collection targets the active tab's collection, else the selected one.</summary>
    public RequestCollection? RunTarget => Tab?.CollectionProvider?.Invoke() ?? SelectedCollection;

    /// <summary>Tooltip for the toolbar's Run Collection button, naming the target or explaining
    /// why the button is disabled.</summary>
    public string RunCollectionToolTip => RunTarget is { } collection
        ? string.Format(System.Globalization.CultureInfo.CurrentCulture, Services.Localization.AppLocalization.Get("Str.Toolbar.RunCollectionTip"), collection.Name)
        : Services.Localization.AppLocalization.Get("Str.Toolbar.RunCollectionDisabledTip");

    public bool IsDarkTheme => _main.Theme == AppTheme.Dark;
    public bool IsLightTheme => _main.Theme == AppTheme.Light;
    public bool IsWin7LightTheme => _main.Theme == AppTheme.Win7Light;

    private bool HasCollection() => SelectedCollection is not null;
    private bool HasFolder() => SelectedFolder is not null;
    private bool HasTab() => Tab is not null;
    private bool HasSavedRequest() => ActiveRequest is not null;
    private bool HasRunTarget() => RunTarget is not null;

    [RelayCommand(CanExecute = nameof(HasCollection))]
    private void AddRequest()
    {
        // Add next to what is selected: inside the selected folder, or the folder of the selected request.
        var folderId = _main.CollectionsPanel.SelectedNode switch
        {
            FolderTreeNode node => node.Folder.Id,
            RequestItem request => request.FolderId,
            _ => null
        };
        if (folderId is { } id && SelectedCollection?.Folders.FirstOrDefault(folder => folder.Id == id) is { } target)
            _main.CollectionsPanel.AddRequestToFolderCommand.Execute(target);
        else
            _main.CollectionsPanel.AddRequestCommand.Execute(SelectedCollection);
    }

    [RelayCommand(CanExecute = nameof(HasCollection))]
    private void AddFolder() => _main.CollectionsPanel.AddFolderCommand.Execute(SelectedCollection);

    [RelayCommand(CanExecute = nameof(HasCollection))]
    private void RenameCollection()
    {
        if (SelectedCollection is not { } collection) return;
        collection.RenameOriginalName = collection.Name;
        collection.IsEditing = true;
    }

    [RelayCommand(CanExecute = nameof(HasCollection))]
    private void DuplicateCollection() => _main.CollectionsPanel.DuplicateCollectionCommand.Execute(SelectedCollection);

    [RelayCommand(CanExecute = nameof(HasCollection))]
    private void RunCollection()
    {
        if (SelectedCollection is { } collection) Dialogs?.OpenCollectionRunner(collection, null);
    }

    [RelayCommand(CanExecute = nameof(HasFolder))]
    private void RunFolder()
    {
        if (SelectedCollection is { } collection && SelectedFolder is { } folder) Dialogs?.OpenCollectionRunner(collection, folder);
    }

    [RelayCommand(CanExecute = nameof(HasCollection))]
    private void CollectionEnvironments() => Dialogs?.OpenEnvironments(SelectedCollection);

    [RelayCommand(CanExecute = nameof(HasCollection))]
    private void CollectionSettings()
    {
        if (SelectedCollection is { } collection) Dialogs?.OpenCollectionSettings(collection);
    }

    [RelayCommand(CanExecute = nameof(HasCollection))]
    private Task ExportCollectionAsync() =>
        SelectedCollection is { } collection && Dialogs is { } dialogs ? dialogs.ExportCollectionAsync(collection, false) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(HasCollection))]
    private Task ExportCollectionAsPostmanAsync() =>
        SelectedCollection is { } collection && Dialogs is { } dialogs ? dialogs.ExportCollectionAsync(collection, true) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(HasCollection))]
    private Task DeleteCollectionAsync() => _main.CollectionsPanel.DeleteCollectionCommand.ExecuteAsync(SelectedCollection);

    /// <summary>Tools > Run Collection and the toolbar button.</summary>
    [RelayCommand(CanExecute = nameof(HasRunTarget))]
    private void RunActiveCollection()
    {
        if (RunTarget is { } collection) Dialogs?.OpenCollectionRunner(collection, null);
    }

    [RelayCommand(CanExecute = nameof(HasTab))]
    private void Send()
    {
        if (Tab?.SendCommand is { } command && command.CanExecute(null)) command.Execute(null);
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => Tab?.CancelCommand.Execute(null);

    private bool CanCancel() => Tab is { IsSending: true };

    [RelayCommand(CanExecute = nameof(HasTab))]
    private Task CopyAsCurlRedactedAsync() => Tab is { } tab && Dialogs is { } dialogs ? dialogs.CopyAsCurlAsync(tab, false) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(HasTab))]
    private Task CopyAsCurlRevealedAsync() => Tab is { } tab && Dialogs is { } dialogs ? dialogs.CopyAsCurlAsync(tab, true) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(HasTab))]
    private void InspectVariables()
    {
        if (Tab is { } tab) Dialogs?.InspectVariables(tab);
    }

    [RelayCommand(CanExecute = nameof(HasSavedRequest))]
    private Task MoveRequestToAsync() => _main.CollectionsPanel.MoveRequestToCommand.ExecuteAsync(ActiveRequest);

    [RelayCommand(CanExecute = nameof(HasSavedRequest))]
    private Task DuplicateRequestAsync() => _main.CollectionsPanel.DuplicateRequestCommand.ExecuteAsync(ActiveRequest);

    [RelayCommand(CanExecute = nameof(HasTab))]
    private Task CloseTabAsync() => _main.CloseTabCommand.ExecuteAsync(Tab);

    /// <summary>The previous recorded send of the active tab's request, compared against its
    /// current response. Null when the tab has no response or the request was never sent before.</summary>
    public HistoryEntry? PreviousSendOfActiveTab
    {
        get
        {
            if (Tab is not { } tab || tab.ResponseStatusClass is ResponseStatusClass.None or ResponseStatusClass.Sending) return null;
            var current = _main.LastHistoryEntryIdOf(tab);
            return _main.History.AllEntries.FirstOrDefault(entry => entry.SourceRequestId == tab.RequestId && entry.Id != current);
        }
    }

    private bool CanCompareResponses() => PreviousSendOfActiveTab is not null;

    [RelayCommand(CanExecute = nameof(CanCompareResponses))]
    private void CompareResponses()
    {
        if (PreviousSendOfActiveTab is { } entry && _main.CompareHistoryEntryWithCurrentTab(entry) is { } comparison)
            Dialogs?.ShowComparison(comparison);
    }

    [RelayCommand]
    private void SetTheme(AppTheme theme) => _main.Theme = theme;

    [RelayCommand]
    private void OpenHelp(string? topicId) => Dialogs?.OpenHelp(topicId);

    /// <summary>View > History Window, the toolbar History button and the navigator link.</summary>
    [RelayCommand]
    private void OpenHistoryWindow() => Dialogs?.OpenHistoryWindow();

    /// <summary>View > Console, Tools > Console and the toolbar Console button.</summary>
    [RelayCommand]
    private void OpenConsole() => Dialogs?.OpenConsole();

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.ActiveTab))
        {
            ObserveTab(_main.ActiveTab);
            Refresh();
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.Theme))
        {
            OnPropertyChanged(nameof(IsDarkTheme));
            OnPropertyChanged(nameof(IsLightTheme));
            OnPropertyChanged(nameof(IsWin7LightTheme));
        }
    }

    private void ObserveTab(RequestTabViewModel? tab)
    {
        if (_observedTab is not null) _observedTab.PropertyChanged -= OnTabPropertyChanged;
        _observedTab = tab;
        if (tab is not null) tab.PropertyChanged += OnTabPropertyChanged;
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RequestTabViewModel.IsSending) or nameof(RequestTabViewModel.ResponseStatusClass)
            or nameof(RequestTabViewModel.RequestId))
            Refresh();
    }

    /// <summary>Re-evaluates every command's availability after a target changed.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(SelectedCollection));
        OnPropertyChanged(nameof(SelectedFolder));
        OnPropertyChanged(nameof(ActiveRequest));
        OnPropertyChanged(nameof(RunTarget));
        OnPropertyChanged(nameof(RunCollectionToolTip));
        foreach (var command in new IRelayCommand[]
                 {
                     AddRequestCommand, AddFolderCommand, RenameCollectionCommand, DuplicateCollectionCommand,
                     RunCollectionCommand, RunFolderCommand, CollectionEnvironmentsCommand, CollectionSettingsCommand,
                     ExportCollectionCommand, ExportCollectionAsPostmanCommand, DeleteCollectionCommand,
                     RunActiveCollectionCommand, SendCommand, CancelCommand, CopyAsCurlRedactedCommand,
                     CopyAsCurlRevealedCommand, InspectVariablesCommand, MoveRequestToCommand, DuplicateRequestCommand,
                     CloseTabCommand, CompareResponsesCommand
                 })
            command.NotifyCanExecuteChanged();
    }
}
