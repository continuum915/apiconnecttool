using System.Collections.ObjectModel;
using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class AppCommandsViewModelTests
{
    private readonly CollectionFolder _folder = new() { Name = "Admin" };
    private readonly RequestItem _rootRequest = new() { Name = "List orders", Url = "https://orders.test" };
    private readonly RequestItem _folderRequest;
    private readonly RequestCollection _orders;
    private readonly RequestCollection _billing = new() { Name = "Billing" };
    private readonly MainWindowViewModel _vm;
    private readonly RecordingDialogs _dialogs = new();

    public AppCommandsViewModelTests()
    {
        _folderRequest = new RequestItem { Name = "Purge", Url = "https://orders.test/purge", FolderId = _folder.Id };
        _orders = new RequestCollection { Name = "Orders", Folders = { _folder }, Requests = { _rootRequest, _folderRequest } };
        var service = new FakeWorkspaceService
        {
            WorkspaceToReturn = new Workspace { Collections = new ObservableCollection<RequestCollection> { _orders, _billing } }
        };
        _vm = new MainWindowViewModel(service, historyStore: new InMemoryHistoryStore());
        _vm.Commands.Dialogs = _dialogs;
    }

    private AppCommandsViewModel Commands => _vm.Commands;

    private CollectionTreeNode Node(RequestCollection collection) => _vm.CollectionsPanel.Tree.Single(node => node.Collection == collection);

    private IEnumerable<System.Windows.Input.ICommand> CollectionCommands =>
    [
        Commands.AddRequestCommand, Commands.AddFolderCommand, Commands.RenameCollectionCommand,
        Commands.DuplicateCollectionCommand, Commands.RunCollectionCommand, Commands.CollectionEnvironmentsCommand,
        Commands.CollectionSettingsCommand, Commands.ExportCollectionCommand, Commands.ExportCollectionAsPostmanCommand,
        Commands.DeleteCollectionCommand
    ];

    private IEnumerable<System.Windows.Input.ICommand> TabCommands =>
    [
        Commands.SendCommand, Commands.CopyAsCurlRedactedCommand, Commands.CopyAsCurlRevealedCommand,
        Commands.InspectVariablesCommand, Commands.CloseTabCommand
    ];

    [Fact]
    public void WithoutSelectionOrTab_CollectionAndRequestCommandsAreDisabled()
    {
        Assert.All(CollectionCommands, command => Assert.False(command.CanExecute(null)));
        Assert.False(Commands.RunFolderCommand.CanExecute(null));
        Assert.All(TabCommands, command => Assert.False(command.CanExecute(null)));
        Assert.False(Commands.CancelCommand.CanExecute(null));
        Assert.False(Commands.MoveRequestToCommand.CanExecute(null));
        Assert.False(Commands.DuplicateRequestCommand.CanExecute(null));
        Assert.False(Commands.RunActiveCollectionCommand.CanExecute(null));
        Assert.False(Commands.CompareResponsesCommand.CanExecute(null));
    }

    [Fact]
    public void SelectingACollection_EnablesCollectionCommandsButNotRunFolder()
    {
        var changed = new List<System.Windows.Input.ICommand>();
        Commands.AddRequestCommand.CanExecuteChanged += (sender, _) => changed.Add((System.Windows.Input.ICommand)sender!);

        _vm.CollectionsPanel.SelectedNode = Node(_billing);

        Assert.Same(_billing, Commands.SelectedCollection);
        Assert.All(CollectionCommands, command => Assert.True(command.CanExecute(null)));
        Assert.False(Commands.RunFolderCommand.CanExecute(null));
        Assert.NotEmpty(changed);
    }

    [Fact]
    public void SelectingAFolderOrRequest_TargetsItsCollection()
    {
        var folderNode = Node(_orders).Folders.Single();
        _vm.CollectionsPanel.SelectedNode = folderNode;
        Assert.Same(_orders, Commands.SelectedCollection);
        Assert.Same(_folder, Commands.SelectedFolder);
        Assert.True(Commands.RunFolderCommand.CanExecute(null));

        Commands.RunFolderCommand.Execute(null);
        Assert.Equal(("runner", _orders, _folder), _dialogs.Calls.Single());

        _vm.CollectionsPanel.SelectedNode = _folderRequest;
        Assert.Same(_orders, Commands.SelectedCollection);
        Assert.Null(Commands.SelectedFolder);
        Assert.False(Commands.RunFolderCommand.CanExecute(null));
    }

    [Fact]
    public void AddRequest_GoesIntoTheSelectedFolderOrTheSelectedRequestsFolder()
    {
        var folderNode = Node(_orders).Folders.Single();

        _vm.CollectionsPanel.SelectedNode = folderNode;
        Commands.AddRequestCommand.Execute(null);
        _vm.CollectionsPanel.SelectedNode = _folderRequest;
        Commands.AddRequestCommand.Execute(null);
        _vm.CollectionsPanel.SelectedNode = _rootRequest;
        Commands.AddRequestCommand.Execute(null);

        var added = _orders.Requests.Where(request => request.Name.StartsWith("New Request")).ToList();
        Assert.Equal(3, added.Count);
        Assert.Equal(_folder.Id, added[0].FolderId);
        Assert.Equal(_folder.Id, added[1].FolderId);
        Assert.Null(added[2].FolderId);
    }

    [Fact]
    public void CollectionCommands_ReachTheSameOperationsAsTheRowActions()
    {
        _vm.CollectionsPanel.SelectedNode = Node(_billing);

        Commands.AddRequestCommand.Execute(null);
        Assert.Single(_billing.Requests);

        Commands.AddFolderCommand.Execute(null);
        Assert.Single(_billing.Folders);

        Commands.RenameCollectionCommand.Execute(null);
        Assert.True(_billing.IsEditing);
        Assert.Equal("Billing", _billing.RenameOriginalName);

        Commands.RunCollectionCommand.Execute(null);
        Commands.CollectionEnvironmentsCommand.Execute(null);
        Commands.CollectionSettingsCommand.Execute(null);
        Commands.ExportCollectionCommand.Execute(null);
        Commands.ExportCollectionAsPostmanCommand.Execute(null);
        Assert.Equal(new (string, object?, object?)[]
        {
            ("runner", _billing, null), ("environments", _billing, null), ("settings", _billing, null),
            ("export", _billing, false), ("export", _billing, true)
        }, _dialogs.Calls);

        var collectionCount = _vm.CollectionsPanel.Collections.Count;
        Commands.DuplicateCollectionCommand.Execute(null);
        Assert.Equal(collectionCount + 1, _vm.CollectionsPanel.Collections.Count);
    }

    [Fact]
    public void RequestCommands_FollowTheActiveTab()
    {
        _vm.AddTabCommand.Execute(null);
        Assert.All(TabCommands, command => Assert.True(command.CanExecute(null)));
        Assert.False(Commands.MoveRequestToCommand.CanExecute(null));
        Assert.False(Commands.DuplicateRequestCommand.CanExecute(null));
        Assert.False(Commands.CancelCommand.CanExecute(null));

        _vm.OpenRequestCommand.Execute(_rootRequest);
        Assert.Same(_rootRequest, Commands.ActiveRequest);
        Assert.True(Commands.MoveRequestToCommand.CanExecute(null));
        Assert.True(Commands.DuplicateRequestCommand.CanExecute(null));

        Commands.InspectVariablesCommand.Execute(null);
        Commands.CopyAsCurlRedactedCommand.Execute(null);
        Commands.CopyAsCurlRevealedCommand.Execute(null);
        Assert.Equal(new (string, object?, object?)[]
        {
            ("inspect", _vm.ActiveTab, null), ("curl", _vm.ActiveTab, false), ("curl", _vm.ActiveTab, true)
        }, _dialogs.Calls);

        var openTabs = _vm.OpenRequests.Count;
        Commands.CloseTabCommand.Execute(null);
        Assert.Equal(openTabs - 1, _vm.OpenRequests.Count);
    }

    [Fact]
    public void RunActiveCollection_PrefersTheActiveTabsCollection()
    {
        _vm.CollectionsPanel.SelectedNode = Node(_billing);
        Assert.Same(_billing, Commands.RunTarget);

        _vm.OpenRequestCommand.Execute(_rootRequest);
        Assert.Same(_orders, Commands.RunTarget);

        Commands.RunActiveCollectionCommand.Execute(null);
        Assert.Equal(("runner", _orders, null), _dialogs.Calls.Single());
    }

    [Fact]
    public void ThemeItems_StayInSyncWithPreferences()
    {
        var raised = new List<string?>();
        Commands.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Commands.SetThemeCommand.Execute(AppTheme.Dark);
        Assert.Equal(AppTheme.Dark, _vm.Theme);
        Assert.True(Commands.IsDarkTheme);
        Assert.False(Commands.IsLightTheme);

        _vm.Theme = AppTheme.Light;
        Assert.True(Commands.IsLightTheme);
        Assert.False(Commands.IsDarkTheme);
        Assert.Contains(nameof(AppCommandsViewModel.IsLightTheme), raised);

        _vm.Theme = AppTheme.Win7Light;
        Assert.True(Commands.IsWin7LightTheme);
    }

    [Fact]
    public void CompareResponses_NeedsAResponseAndAnEarlierSendOfTheSameRequest()
    {
        _vm.OpenRequestCommand.Execute(_rootRequest);
        var tab = _vm.ActiveTab!;
        tab.ResponseStatusClass = ResponseStatusClass.Success;
        Assert.False(Commands.CompareResponsesCommand.CanExecute(null));

        _vm.History.Record(new HistoryEntry { SourceRequestId = _rootRequest.Id, StatusCode = 200, SentAtUtc = DateTimeOffset.UtcNow });
        Assert.True(Commands.CompareResponsesCommand.CanExecute(null));

        Commands.CompareResponsesCommand.Execute(null);
        Assert.Equal("compare", _dialogs.Calls.Single().Item1);
    }

    private sealed class RecordingDialogs : IAppDialogs
    {
        public List<(string, object?, object?)> Calls { get; } = new();
        public void OpenCollectionRunner(RequestCollection collection, CollectionFolder? folder) => Calls.Add(("runner", collection, folder));
        public void OpenCollectionSettings(RequestCollection collection) => Calls.Add(("settings", collection, null));
        public void OpenEnvironments(RequestCollection? collection) => Calls.Add(("environments", collection, null));
        public Task ExportCollectionAsync(RequestCollection collection, bool asPostman) { Calls.Add(("export", collection, asPostman)); return Task.CompletedTask; }
        public Task CopyAsCurlAsync(RequestTabViewModel tab, bool revealSecrets) { Calls.Add(("curl", tab, revealSecrets)); return Task.CompletedTask; }
        public void InspectVariables(RequestTabViewModel tab) => Calls.Add(("inspect", tab, null));
        public void ShowComparison(ResponseComparisonViewModel comparison) => Calls.Add(("compare", comparison, null));
        public void OpenHelp(string? topicId) => Calls.Add(("help", topicId, null));
        public void OpenHistoryWindow() => Calls.Add(("history", null, null));
        public void OpenConsole() => Calls.Add(("console", null, null));
    }
}
