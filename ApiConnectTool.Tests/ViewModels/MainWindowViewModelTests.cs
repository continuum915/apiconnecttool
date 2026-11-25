using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class MainWindowViewModelTests
{
    private static (MainWindowViewModel vm, FakeWorkspaceService service) CreateVm(Workspace? workspace = null)
    {
        var service = new FakeWorkspaceService { WorkspaceToReturn = workspace ?? new Workspace() };
        return (new MainWindowViewModel(service), service);
    }

    [Fact]
    public void EmptyWorkspace_StartsWithoutSampleCollectionsOrTabs()
    {
        var (vm, _) = CreateVm();

        Assert.Empty(vm.CollectionsPanel.Collections);
        Assert.Empty(vm.OpenRequests);
        Assert.Null(vm.ActiveTab);
    }

    [Fact]
    public void AddTab_CreatesUnsavedDraft()
    {
        var (vm, _) = CreateVm();

        vm.AddTabCommand.Execute(null);

        Assert.Single(vm.OpenRequests);
        Assert.Null(vm.ActiveTab!.RequestId);
    }

    [Fact]
    public void Save_NewTab_CreatesRequestAndCollection()
    {
        var (vm, service) = CreateVm();
        vm.AddTabCommand.Execute(null);
        vm.ActiveTab!.Name = "List pets";
        vm.ActiveTab.RequestUrl = "https://api.example.test/pets";
        vm.ActiveTab.RequestHeaders[0].Key = "Accept";
        vm.ActiveTab.RequestHeaders[0].Value = "application/json";

        vm.SaveCommand.Execute(null);

        var saved = Assert.Single(Assert.Single(vm.CollectionsPanel.Collections).Requests);
        Assert.Equal("List pets", saved.Name);
        Assert.Equal("Accept", saved.Headers[0].Key);
        Assert.Equal(saved.Id, vm.ActiveTab.RequestId);
        Assert.False(vm.ActiveTab.IsDirty);
        Assert.True(service.SaveCallCount > 0);
    }

    [Fact]
    public void OpenRequest_UsesIdSoSameNamedRequestsOpenIndependently()
    {
        var first = new RequestItem { Id = Guid.NewGuid(), Name = "Get users", Url = "https://one.test" };
        var second = new RequestItem { Id = Guid.NewGuid(), Name = "Get users", Url = "https://two.test" };
        var workspace = new Workspace
        {
            Collections = new ObservableCollection<RequestCollection>
            {
                new() { Name = "One", Requests = new ObservableCollection<RequestItem> { first } },
                new() { Name = "Two", Requests = new ObservableCollection<RequestItem> { second } }
            }
        };
        var (vm, _) = CreateVm(workspace);

        vm.OpenRequestCommand.Execute(first);
        vm.OpenRequestCommand.Execute(second);

        Assert.Equal(2, vm.OpenRequests.Count);
        Assert.Equal(second.Id, vm.ActiveTab!.RequestId);
    }

    [Fact]
    public void CloseTab_DoesNotDiscardDirtyTab()
    {
        var (vm, _) = CreateVm();
        vm.AddTabCommand.Execute(null);
        vm.ActiveTab!.RequestUrl = "https://api.example.test";

        vm.CloseTabCommand.Execute(vm.ActiveTab);

        Assert.Single(vm.OpenRequests);
    }

    [Fact]
    public void DiscardAndCloseTab_RemovesDirtyTab()
    {
        var (vm, _) = CreateVm();
        vm.AddTabCommand.Execute(null);
        vm.ActiveTab!.RequestUrl = "https://api.example.test";

        vm.DiscardAndCloseTabCommand.Execute(vm.ActiveTab);

        Assert.Empty(vm.OpenRequests);
    }

    private static MainWindowViewModel CreateVmWithDirtyTab(UnsavedChangesChoice choice, List<IReadOnlyList<RequestTabViewModel>> prompts, FakeWorkspaceService? service = null)
    {
        service ??= new FakeWorkspaceService();
        var vm = new MainWindowViewModel(service);
        vm.UnsavedChangesPromptHandler = tabs =>
        {
            prompts.Add(tabs);
            return Task.FromResult(choice);
        };
        vm.AddTabCommand.Execute(null);
        vm.ActiveTab!.Name = "Draft";
        vm.ActiveTab.RequestUrl = "https://api.example.test";
        return vm;
    }

    [Fact]
    public async Task CloseTab_CleanTab_ClosesWithoutPrompting()
    {
        var prompts = new List<IReadOnlyList<RequestTabViewModel>>();
        var (vm, _) = CreateVm();
        vm.UnsavedChangesPromptHandler = tabs => { prompts.Add(tabs); return Task.FromResult(UnsavedChangesChoice.Cancel); };
        vm.AddTabCommand.Execute(null);

        await vm.CloseTabCommand.ExecuteAsync(vm.ActiveTab);

        Assert.Empty(vm.OpenRequests);
        Assert.Empty(prompts);
    }

    [Fact]
    public async Task CloseTab_DirtyTab_CancelKeepsTabAndEdits()
    {
        var prompts = new List<IReadOnlyList<RequestTabViewModel>>();
        var vm = CreateVmWithDirtyTab(UnsavedChangesChoice.Cancel, prompts);
        var tab = vm.ActiveTab!;

        await vm.CloseTabCommand.ExecuteAsync(tab);

        Assert.Same(tab, Assert.Single(prompts).Single());
        Assert.Same(tab, Assert.Single(vm.OpenRequests));
        Assert.True(tab.IsDirty);
        Assert.Equal("https://api.example.test", tab.RequestUrl);
    }

    [Fact]
    public async Task CloseTab_DirtyTab_DiscardClosesWithoutSaving()
    {
        var prompts = new List<IReadOnlyList<RequestTabViewModel>>();
        var vm = CreateVmWithDirtyTab(UnsavedChangesChoice.Discard, prompts);

        await vm.CloseTabCommand.ExecuteAsync(vm.ActiveTab);

        Assert.Empty(vm.OpenRequests);
        Assert.Empty(vm.CollectionsPanel.Collections);
    }

    [Fact]
    public async Task CloseTab_DirtyTab_SaveSavesThenCloses()
    {
        var prompts = new List<IReadOnlyList<RequestTabViewModel>>();
        var vm = CreateVmWithDirtyTab(UnsavedChangesChoice.Save, prompts);

        await vm.CloseTabCommand.ExecuteAsync(vm.ActiveTab);

        Assert.Empty(vm.OpenRequests);
        var saved = Assert.Single(Assert.Single(vm.CollectionsPanel.Collections).Requests);
        Assert.Equal("Draft", saved.Name);
        Assert.Equal("https://api.example.test", saved.Url);
    }

    [Fact]
    public async Task CloseTab_SaveExistingRequest_ReopeningFromSidebarShowsSavedChanges()
    {
        var request = new RequestItem { Id = Guid.NewGuid(), Name = "Update User", Url = "https://api.example.test/users/1" };
        var workspace = new Workspace
        {
            Collections = new ObservableCollection<RequestCollection>
            {
                new() { Name = "My API", Requests = new ObservableCollection<RequestItem> { request } }
            }
        };
        var (vm, service) = CreateVm(workspace);
        vm.UnsavedChangesPromptHandler = _ => Task.FromResult(UnsavedChangesChoice.Save);
        vm.OpenRequestCommand.Execute(vm.CollectionsPanel.Tree[0].Requests[0]);
        vm.ActiveTab!.RequestUrl = "https://api.example.test/users/2";

        await vm.CloseTabCommand.ExecuteAsync(vm.ActiveTab);
        vm.OpenRequestCommand.Execute(vm.CollectionsPanel.Tree[0].Requests[0]);

        Assert.Equal("https://api.example.test/users/2", service.LastSaved!.Collections[0].Requests[0].Url);
        Assert.Equal("https://api.example.test/users/2", vm.ActiveTab!.RequestUrl);
    }

    [Fact]
    public void Save_ExistingRequest_SearchResultsShowSavedVersion()
    {
        var request = new RequestItem { Id = Guid.NewGuid(), Name = "Update User", Url = "https://api.example.test/users/1" };
        var workspace = new Workspace
        {
            Collections = new ObservableCollection<RequestCollection>
            {
                new() { Name = "My API", Requests = new ObservableCollection<RequestItem> { request } }
            }
        };
        var (vm, _) = CreateVm(workspace);
        vm.CollectionsPanel.SearchText = "Update";
        vm.OpenRequestCommand.Execute(request);
        vm.ActiveTab!.RequestUrl = "https://api.example.test/users/2";

        vm.SaveCommand.Execute(null);

        Assert.Equal("https://api.example.test/users/2", Assert.Single(vm.CollectionsPanel.SearchResults).Url);
    }

    [Fact]
    public async Task CloseTab_DirtyTab_SaveFailureKeepsTabOpenAndDirty()
    {
        var prompts = new List<IReadOnlyList<RequestTabViewModel>>();
        var service = new FakeWorkspaceService();
        var vm = CreateVmWithDirtyTab(UnsavedChangesChoice.Save, prompts, service);
        var tab = vm.ActiveTab!;
        service.SaveException = new System.IO.IOException("disk full");

        await vm.CloseTabCommand.ExecuteAsync(tab);

        Assert.Same(tab, Assert.Single(vm.OpenRequests));
        Assert.True(tab.IsDirty);
    }

    [Fact]
    public async Task ConfirmExit_NoDirtyTabs_ReturnsTrueWithoutPrompting()
    {
        var prompts = new List<IReadOnlyList<RequestTabViewModel>>();
        var (vm, _) = CreateVm();
        vm.UnsavedChangesPromptHandler = tabs => { prompts.Add(tabs); return Task.FromResult(UnsavedChangesChoice.Cancel); };
        vm.AddTabCommand.Execute(null);

        Assert.True(await vm.ConfirmExitAsync());
        Assert.Empty(prompts);
    }

    [Fact]
    public async Task ConfirmExit_PromptsOnceWithOnlyTheDirtyTabs()
    {
        var prompts = new List<IReadOnlyList<RequestTabViewModel>>();
        var vm = CreateVmWithDirtyTab(UnsavedChangesChoice.Cancel, prompts);
        var dirty = vm.ActiveTab!;
        vm.AddTabCommand.Execute(null); // clean second tab

        Assert.False(await vm.ConfirmExitAsync());

        Assert.Same(dirty, Assert.Single(Assert.Single(prompts)));
        Assert.Equal(2, vm.OpenRequests.Count);
    }

    [Fact]
    public async Task ConfirmExit_SaveSavesEveryDirtyTab()
    {
        var prompts = new List<IReadOnlyList<RequestTabViewModel>>();
        var vm = CreateVmWithDirtyTab(UnsavedChangesChoice.Save, prompts);
        vm.AddTabCommand.Execute(null);
        vm.ActiveTab!.Name = "Second";
        vm.ActiveTab.RequestUrl = "https://api.example.test/second";

        Assert.True(await vm.ConfirmExitAsync());

        Assert.Equal(2, Assert.Single(prompts).Count);
        Assert.All(vm.OpenRequests, tab => Assert.False(tab.IsDirty));
        Assert.Equal(2, Assert.Single(vm.CollectionsPanel.Collections).Requests.Count);
    }

    [Fact]
    public async Task ConfirmExit_DiscardAllowsExitWithoutSaving()
    {
        var prompts = new List<IReadOnlyList<RequestTabViewModel>>();
        var vm = CreateVmWithDirtyTab(UnsavedChangesChoice.Discard, prompts);

        Assert.True(await vm.ConfirmExitAsync());
        Assert.Empty(vm.CollectionsPanel.Collections);
    }

    [Fact]
    public async Task ConfirmExit_SaveFailureBlocksExit()
    {
        var prompts = new List<IReadOnlyList<RequestTabViewModel>>();
        var service = new FakeWorkspaceService();
        var vm = CreateVmWithDirtyTab(UnsavedChangesChoice.Save, prompts, service);
        service.SaveException = new UnauthorizedAccessException("read-only");

        Assert.False(await vm.ConfirmExitAsync());
        Assert.True(vm.ActiveTab!.IsDirty);
    }

    [Fact]
    public void Save_DiskFailure_ShowsErrorKeepsTabDirtyAndRollsBackNewRequest()
    {
        var (vm, service) = CreateVm();
        vm.AddTabCommand.Execute(null);
        vm.ActiveTab!.RequestUrl = "https://api.example.test";
        service.SaveException = new System.IO.IOException("disk full");

        vm.SaveCommand.Execute(null);

        Assert.Equal("disk full", vm.WorkspaceSaveError);
        Assert.True(vm.ActiveTab.IsDirty);
        Assert.Null(vm.ActiveTab.RequestId);
        Assert.Empty(vm.CollectionsPanel.Collections);
        Assert.Empty(vm.CollectionsPanel.Tree);
    }

    [Fact]
    public void Save_DiskFailure_ThenRetrySucceeds_SavesOnceAndClearsError()
    {
        var (vm, service) = CreateVm();
        vm.AddTabCommand.Execute(null);
        vm.ActiveTab!.RequestUrl = "https://api.example.test";
        service.SaveException = new System.IO.IOException("disk full");
        vm.SaveCommand.Execute(null);

        service.SaveException = null;
        vm.SaveCommand.Execute(null);

        Assert.Null(vm.WorkspaceSaveError);
        Assert.False(vm.ActiveTab.IsDirty);
        Assert.Single(Assert.Single(vm.CollectionsPanel.Collections).Requests);
    }

    [Fact]
    public void Save_ExistingRequestDiskFailure_RestoresPreviousSavedVersion()
    {
        var request = new RequestItem { Id = Guid.NewGuid(), Name = "Get pets", Url = "https://api.example.test/old" };
        var workspace = new Workspace
        {
            Collections = new ObservableCollection<RequestCollection>
            {
                new() { Name = "Pets", Requests = new ObservableCollection<RequestItem> { request } }
            }
        };
        var (vm, service) = CreateVm(workspace);
        vm.OpenRequestCommand.Execute(request);
        vm.ActiveTab!.RequestUrl = "https://api.example.test/new";
        service.SaveException = new System.IO.IOException("disk full");

        vm.SaveCommand.Execute(null);

        Assert.Same(request, Assert.Single(vm.CollectionsPanel.Collections[0].Requests));
        Assert.Equal("https://api.example.test/old", vm.CollectionsPanel.Collections[0].Requests[0].Url);
        Assert.True(vm.ActiveTab.IsDirty);
    }

    [Fact]
    public async Task SaveAs_DiskFailure_KeepsTabPointingAtOriginal()
    {
        var request = new RequestItem { Id = Guid.NewGuid(), Name = "Get pets", Url = "https://api.example.test/pets" };
        var workspace = new Workspace
        {
            Collections = new ObservableCollection<RequestCollection>
            {
                new() { Name = "Pets", Requests = new ObservableCollection<RequestItem> { request } }
            }
        };
        var (vm, service) = CreateVm(workspace);
        vm.OpenRequestCommand.Execute(request);
        service.SaveException = new System.IO.IOException("disk full");

        await vm.SaveAsCommand.ExecuteAsync(null);

        Assert.Single(vm.CollectionsPanel.Collections[0].Requests);
        Assert.Equal(request.Id, vm.ActiveTab!.RequestId);
        Assert.Equal("Get pets", vm.ActiveTab.Name);
        Assert.NotNull(vm.WorkspaceSaveError);
    }

    [Fact]
    public void SidebarCommand_DiskFailure_KeepsChangeInMemoryAndDoesNotThrow()
    {
        var (vm, service) = CreateVm();
        service.SaveException = new UnauthorizedAccessException("read-only");

        vm.CollectionsPanel.AddCollectionCommand.Execute(null);

        Assert.Single(vm.CollectionsPanel.Collections);
        Assert.Equal("read-only", vm.WorkspaceSaveError);
    }

    [Fact]
    public async Task ConfirmExit_UnpersistedSidebarChange_RetrySucceeds_Exits()
    {
        var (vm, service) = CreateVm();
        service.SaveException = new System.IO.IOException("disk full");
        vm.CollectionsPanel.AddCollectionCommand.Execute(null);
        var exitPromptShown = false;
        vm.ExitWithUnsavedWorkspacePromptHandler = _ => { exitPromptShown = true; return Task.FromResult(false); };
        service.SaveException = null;

        Assert.True(await vm.ConfirmExitAsync());
        Assert.False(exitPromptShown);
        Assert.Single(service.LastSaved!.Collections);
    }

    [Fact]
    public async Task ConfirmExit_UnpersistedSidebarChange_RetryFails_AsksAndHonorsAnswer()
    {
        var (vm, service) = CreateVm();
        service.SaveException = new System.IO.IOException("disk full");
        vm.CollectionsPanel.AddCollectionCommand.Execute(null);
        string? shownDetail = null;
        var answer = false;
        vm.ExitWithUnsavedWorkspacePromptHandler = detail => { shownDetail = detail; return Task.FromResult(answer); };

        Assert.False(await vm.ConfirmExitAsync());
        Assert.Equal("disk full", shownDetail);

        answer = true;
        Assert.True(await vm.ConfirmExitAsync());
    }

    [Fact]
    public async Task ConfirmExit_UnpersistedSidebarChange_NoHandler_BlocksExit()
    {
        var (vm, service) = CreateVm();
        service.SaveException = new System.IO.IOException("disk full");
        vm.CollectionsPanel.AddCollectionCommand.Execute(null);

        Assert.False(await vm.ConfirmExitAsync());
    }

    [Fact]
    public void UndecryptableSecretsBanner_ListsLocationsAndCanBeDismissed()
    {
        var service = new FakeWorkspaceService
        {
            UndecryptableSecrets =
            [
                new(ApiConnectTool.Services.UndecryptableSecretLocationKind.RequestAuth, "List pets", ApiConnectTool.Services.UndecryptableSecretField.ClientSecret),
                new(ApiConnectTool.Services.UndecryptableSecretLocationKind.GlobalVariable, string.Empty, ApiConnectTool.Services.UndecryptableSecretField.InitialValue, "token"),
            ]
        };
        var vm = new MainWindowViewModel(service);

        var expected = string.Join(System.Environment.NewLine,
            string.Format(ApiConnectTool.Services.Localization.AppLocalization.Get("Str.Workspace.UndecryptableSecret.RequestAuth"), "List pets",
                ApiConnectTool.Services.Localization.AppLocalization.Get("Str.Workspace.UndecryptableSecret.Field.ClientSecret")),
            string.Format(ApiConnectTool.Services.Localization.AppLocalization.Get("Str.Workspace.UndecryptableSecret.GlobalVariable"), string.Empty,
                ApiConnectTool.Services.Localization.AppLocalization.Get("Str.Workspace.UndecryptableSecret.Field.InitialValue"), "token"));
        Assert.Equal(expected, vm.UndecryptableSecretsBanner.Warning);

        vm.UndecryptableSecretsBanner.DismissCommand.Execute(null);

        Assert.Null(vm.UndecryptableSecretsBanner.Warning);
    }

    [Fact]
    public void UndecryptableSecretsBanner_HiddenWhenEverySecretDecrypts()
    {
        var vm = new MainWindowViewModel(new FakeWorkspaceService());

        Assert.Null(vm.UndecryptableSecretsBanner.Warning);
    }

    [Fact]
    public void DismissCommands_ClearBanners()
    {
        var service = new FakeWorkspaceService { LastLoadError = "load failed" };
        var vm = new MainWindowViewModel(service);
        service.SaveException = new System.IO.IOException("disk full");
        vm.CollectionsPanel.AddCollectionCommand.Execute(null);
        Assert.Equal("load failed", vm.WorkspaceWarning);
        Assert.Equal("disk full", vm.WorkspaceSaveError);

        vm.DismissWorkspaceWarningCommand.Execute(null);
        vm.DismissWorkspaceSaveErrorCommand.Execute(null);

        Assert.Null(vm.WorkspaceWarning);
        Assert.Null(vm.WorkspaceSaveError);
    }

    [Fact]
    public async Task SaveAs_CopiesExistingRequestWithNewIdentity()
    {
        var request = new RequestItem { Id = Guid.NewGuid(), Name = "Get pets", Url = "https://api.example.test/pets" };
        var workspace = new Workspace
        {
            Collections = new ObservableCollection<RequestCollection>
            {
                new() { Name = "Pets", Requests = new ObservableCollection<RequestItem> { request } }
            }
        };
        var (vm, _) = CreateVm(workspace);
        vm.OpenRequestCommand.Execute(request);

        await vm.SaveAsCommand.ExecuteAsync(null);

        var requests = vm.CollectionsPanel.Collections[0].Requests;
        Assert.Equal(2, requests.Count);
        Assert.NotEqual(request.Id, vm.ActiveTab!.RequestId);
        Assert.StartsWith("Copy of Get pets", vm.ActiveTab.Name);
    }

    [Fact]
    public async Task SaveAs_PromptCancelled_LeavesActiveTabAndCollectionUnchanged()
    {
        var request = new RequestItem { Id = Guid.NewGuid(), Name = "Get pets", Url = "https://api.example.test/pets" };
        var workspace = new Workspace
        {
            Collections = new ObservableCollection<RequestCollection>
            {
                new() { Name = "Pets", Requests = new ObservableCollection<RequestItem> { request } }
            }
        };
        var (vm, _) = CreateVm(workspace);
        vm.OpenRequestCommand.Execute(request);
        vm.SaveAsPromptHandler = _ => Task.FromResult<SaveAsPromptResult?>(null);

        await vm.SaveAsCommand.ExecuteAsync(null);

        Assert.Single(vm.CollectionsPanel.Collections[0].Requests);
        Assert.Equal(request.Id, vm.ActiveTab!.RequestId);
    }

    [Fact]
    public async Task SaveAs_PromptResultRenamesTheCopy()
    {
        var request = new RequestItem { Id = Guid.NewGuid(), Name = "Get pets", Url = "https://api.example.test/pets" };
        var workspace = new Workspace
        {
            Collections = new ObservableCollection<RequestCollection>
            {
                new() { Name = "Pets", Requests = new ObservableCollection<RequestItem> { request } }
            }
        };
        var (vm, _) = CreateVm(workspace);
        vm.OpenRequestCommand.Execute(request);
        vm.SaveAsPromptHandler = _ => Task.FromResult<SaveAsPromptResult?>(new SaveAsPromptResult("Get pets (staging)", DontShowAgain: false));

        await vm.SaveAsCommand.ExecuteAsync(null);

        Assert.Equal("Get pets (staging)", vm.ActiveTab!.Name);
        Assert.False(vm.SkipSaveAsConfirmation);
    }

    [Fact]
    public async Task SaveAs_DontShowAgain_PersistsSkipFlagAndSkipsFuturePrompts()
    {
        var request = new RequestItem { Id = Guid.NewGuid(), Name = "Get pets", Url = "https://api.example.test/pets" };
        var workspace = new Workspace
        {
            Collections = new ObservableCollection<RequestCollection>
            {
                new() { Name = "Pets", Requests = new ObservableCollection<RequestItem> { request } }
            }
        };
        var (vm, service) = CreateVm(workspace);
        vm.OpenRequestCommand.Execute(request);
        vm.SaveAsPromptHandler = _ => Task.FromResult<SaveAsPromptResult?>(new SaveAsPromptResult("Copy of Get pets", DontShowAgain: true));

        await vm.SaveAsCommand.ExecuteAsync(null);

        Assert.True(vm.SkipSaveAsConfirmation);
        Assert.True(service.LastSaved?.SkipSaveAsConfirmation);

        var promptInvoked = false;
        vm.SaveAsPromptHandler = _ => { promptInvoked = true; return Task.FromResult<SaveAsPromptResult?>(null); };

        await vm.SaveAsCommand.ExecuteAsync(null);

        Assert.False(promptInvoked);
    }

    [Fact]
    public void SelectedEnvironment_LoadsFromPersistedWorkspaceSelection()
    {
        var environment = new RequestEnvironment { Name = "Staging" };
        var workspace = new Workspace
        {
            Environments = new ObservableCollection<RequestEnvironment> { environment },
            SelectedEnvironmentId = environment.Id
        };

        var (vm, _) = CreateVm(workspace);

        Assert.Same(environment, vm.SelectedEnvironment);
    }

    [Fact]
    public void ChangingSelectedEnvironment_PersistsSelectionImmediately()
    {
        var environment = new RequestEnvironment { Name = "Staging" };
        var workspace = new Workspace { Environments = new ObservableCollection<RequestEnvironment> { environment } };
        var (vm, service) = CreateVm(workspace);
        var saveCountBefore = service.SaveCallCount;

        vm.SelectedEnvironment = environment;

        Assert.True(service.SaveCallCount > saveCountBefore);
        Assert.Equal(environment.Id, service.LastSaved!.SelectedEnvironmentId);
    }

    [Fact]
    public void OnEnvironmentsChanged_ClearsSelectionWhenSelectedEnvironmentWasDeleted()
    {
        var environment = new RequestEnvironment { Name = "Staging" };
        var workspace = new Workspace { Environments = new ObservableCollection<RequestEnvironment> { environment } };
        var (vm, _) = CreateVm(workspace);
        vm.SelectedEnvironment = environment;

        vm.Environments.Remove(environment);
        vm.OnEnvironmentsChanged();

        Assert.Null(vm.SelectedEnvironment);
    }

    [Fact]
    public void AddRequest_AssignsIncrementingSortOrderAtCollectionRoot()
    {
        var (vm, _) = CreateVm();
        vm.CollectionsPanel.AddCollectionCommand.Execute(null);
        var collection = vm.CollectionsPanel.Collections[0];

        vm.CollectionsPanel.AddRequestCommand.Execute(collection);
        vm.CollectionsPanel.AddRequestCommand.Execute(collection);

        Assert.Equal([0, 1], collection.Requests.Select(r => r.SortOrder));
        Assert.All(collection.Requests, r => Assert.Null(r.FolderId));
    }

    [Fact]
    public void Save_ExistingRequest_PreservesFolderIdAndSortOrder()
    {
        var request = new RequestItem { Id = Guid.NewGuid(), Name = "Get pets", Url = "https://api.example.test/pets", FolderId = Guid.NewGuid(), SortOrder = 7 };
        var workspace = new Workspace
        {
            Collections = new ObservableCollection<RequestCollection>
            {
                new() { Name = "Pets", Requests = new ObservableCollection<RequestItem> { request } }
            }
        };
        var (vm, _) = CreateVm(workspace);
        vm.OpenRequestCommand.Execute(request);
        vm.ActiveTab!.RequestUrl = "https://api.example.test/pets/1";

        vm.SaveCommand.Execute(null);

        var saved = vm.CollectionsPanel.Collections[0].Requests[0];
        Assert.Equal(request.FolderId, saved.FolderId);
        Assert.Equal(7, saved.SortOrder);
    }

    [Fact]
    public void RenameCollection_TrimsAndPersists()
    {
        var (vm, service) = CreateVm();
        vm.CollectionsPanel.AddCollectionCommand.Execute(null);
        var collection = vm.CollectionsPanel.Collections[0];
        collection.Name = "  Renamed  ";

        vm.CollectionsPanel.RenameCollectionCommand.Execute(collection);

        Assert.Equal("Renamed", collection.Name);
        Assert.True(service.SaveCallCount > 0);
    }

    [Fact]
    public void RenameCollection_BlankName_FallsBackToDefault()
    {
        var (vm, _) = CreateVm();
        vm.CollectionsPanel.AddCollectionCommand.Execute(null);
        var collection = vm.CollectionsPanel.Collections[0];
        collection.Name = "   ";

        vm.CollectionsPanel.RenameCollectionCommand.Execute(collection);

        Assert.Equal("Untitled Collection", collection.Name);
    }

    [Fact]
    public void RenameRequest_TrimsAndPersists()
    {
        var (vm, _) = CreateVm();
        vm.CollectionsPanel.AddCollectionCommand.Execute(null);
        vm.CollectionsPanel.AddRequestCommand.Execute(vm.CollectionsPanel.Collections[0]);
        var request = vm.CollectionsPanel.Collections[0].Requests[0];
        request.Name = "  Renamed Request  ";

        vm.CollectionsPanel.RenameRequestCommand.Execute(request);

        Assert.Equal("Renamed Request", request.Name);
    }

    [Fact]
    public async Task DuplicateRequest_InsertsCopyWithNewIdAndDisambiguatedNameAfterOriginal()
    {
        var (vm, _) = CreateVm();
        vm.CollectionsPanel.AddCollectionCommand.Execute(null);
        var collection = vm.CollectionsPanel.Collections[0];
        vm.CollectionsPanel.AddRequestCommand.Execute(collection);
        var original = collection.Requests[0];
        original.Name = "Get pets";

        await vm.CollectionsPanel.DuplicateRequestCommand.ExecuteAsync(original);

        Assert.Equal(2, collection.Requests.Count);
        var copy = collection.Requests[1];
        Assert.Equal("Copy of Get pets", copy.Name);
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal(original.FolderId, copy.FolderId);
    }

    [Fact]
    public async Task DuplicateRequest_PromptCancelled_DoesNotInsertCopy()
    {
        var (vm, _) = CreateVm();
        vm.CollectionsPanel.AddCollectionCommand.Execute(null);
        var collection = vm.CollectionsPanel.Collections[0];
        vm.CollectionsPanel.AddRequestCommand.Execute(collection);
        var original = collection.Requests[0];
        original.Name = "Get pets";
        vm.SaveAsPromptHandler = _ => Task.FromResult<SaveAsPromptResult?>(null);

        await vm.CollectionsPanel.DuplicateRequestCommand.ExecuteAsync(original);

        Assert.Single(collection.Requests);
    }

    [Fact]
    public void DuplicateCollection_DeepCopiesFoldersAndRequestsWithNewIds()
    {
        var (vm, _) = CreateVm();
        vm.CollectionsPanel.AddCollectionCommand.Execute(null);
        var collection = vm.CollectionsPanel.Collections[0];
        vm.CollectionsPanel.AddFolderCommand.Execute(collection);
        var folder = collection.Folders[0];
        vm.CollectionsPanel.AddRequestCommand.Execute(collection);
        collection.Requests[0].FolderId = folder.Id;

        vm.CollectionsPanel.DuplicateCollectionCommand.Execute(collection);

        Assert.Equal(2, vm.CollectionsPanel.Collections.Count);
        var copy = vm.CollectionsPanel.Collections[1];
        Assert.StartsWith("Copy of", copy.Name);
        Assert.NotEqual(collection.Id, copy.Id);
        var copiedFolder = Assert.Single(copy.Folders);
        Assert.NotEqual(folder.Id, copiedFolder.Id);
        var copiedRequest = Assert.Single(copy.Requests);
        Assert.Equal(copiedFolder.Id, copiedRequest.FolderId);
    }

    [Fact]
    public void AddFolder_DedupesNameAgainstSiblingTopLevelFolders()
    {
        var (vm, _) = CreateVm();
        vm.CollectionsPanel.AddCollectionCommand.Execute(null);
        var collection = vm.CollectionsPanel.Collections[0];

        vm.CollectionsPanel.AddFolderCommand.Execute(collection);
        vm.CollectionsPanel.AddFolderCommand.Execute(collection);

        Assert.Equal(["New Folder", "New Folder 1"], collection.Folders.Select(f => f.Name));
    }

    [Fact]
    public void RenameFolder_TrimsAndPersists()
    {
        var (vm, _) = CreateVm();
        vm.CollectionsPanel.AddCollectionCommand.Execute(null);
        vm.CollectionsPanel.AddFolderCommand.Execute(vm.CollectionsPanel.Collections[0]);
        var folder = vm.CollectionsPanel.Collections[0].Folders[0];
        folder.Name = "  Auth  ";

        vm.CollectionsPanel.RenameFolderCommand.Execute(folder);

        Assert.Equal("Auth", folder.Name);
    }

    [Fact]
    public async Task DeleteFolder_MovesContainedRequestsToCollectionRootInsteadOfDeletingThem()
    {
        var (vm, _) = CreateVm();
        vm.CollectionsPanel.AddCollectionCommand.Execute(null);
        var collection = vm.CollectionsPanel.Collections[0];
        vm.CollectionsPanel.AddFolderCommand.Execute(collection);
        var folder = collection.Folders[0];
        vm.CollectionsPanel.AddRequestCommand.Execute(collection);
        var request = collection.Requests[0];
        request.FolderId = folder.Id;

        vm.CollectionsPanel.DeleteConfirmationHandler = (_, _) => Task.FromResult(true);
        await vm.CollectionsPanel.DeleteFolderCommand.ExecuteAsync(folder);

        Assert.Empty(collection.Folders);
        Assert.Single(collection.Requests);
        Assert.Null(collection.Requests[0].FolderId);
    }

    [Fact]
    public void MoveRequest_ReordersWithinSameFolderByRenumberingSiblings()
    {
        var (vm, _) = CreateVm();
        vm.CollectionsPanel.AddCollectionCommand.Execute(null);
        var collection = vm.CollectionsPanel.Collections[0];
        vm.CollectionsPanel.AddRequestCommand.Execute(collection);
        vm.CollectionsPanel.AddRequestCommand.Execute(collection);
        var first = collection.Requests[0];
        var second = collection.Requests[1];

        vm.CollectionsPanel.MoveRequest(second, collection, null, targetIndex: 0);

        Assert.Equal(0, second.SortOrder);
        Assert.Equal(1, first.SortOrder);
    }

    [Fact]
    public void MoveRequest_MovesRequestAcrossCollectionsAndIntoTargetFolder()
    {
        var (vm, _) = CreateVm();
        vm.CollectionsPanel.AddCollectionCommand.Execute(null);
        vm.CollectionsPanel.AddCollectionCommand.Execute(null);
        var source = vm.CollectionsPanel.Collections[0];
        var target = vm.CollectionsPanel.Collections[1];
        vm.CollectionsPanel.AddFolderCommand.Execute(target);
        var targetFolder = target.Folders[0];
        vm.CollectionsPanel.AddRequestCommand.Execute(source);
        var request = source.Requests[0];

        vm.CollectionsPanel.MoveRequest(request, target, targetFolder.Id, targetIndex: 0);

        Assert.Empty(source.Requests);
        Assert.Same(request, Assert.Single(target.Requests));
        Assert.Equal(targetFolder.Id, request.FolderId);
        Assert.Equal(0, request.SortOrder);
    }

    [Fact]
    public void SearchText_FiltersRequestsAcrossCollectionsByNameUrlOrMethod()
    {
        var (vm, _) = CreateVm();
        vm.CollectionsPanel.AddCollectionCommand.Execute(null);
        var collection = vm.CollectionsPanel.Collections[0];
        vm.CollectionsPanel.AddRequestCommand.Execute(collection);
        vm.CollectionsPanel.AddRequestCommand.Execute(collection);
        collection.Requests[0].Name = "List pets";
        collection.Requests[0].Url = "https://api.example.test/pets";
        collection.Requests[1].Name = "Get user";
        collection.Requests[1].Url = "https://api.example.test/users";

        vm.CollectionsPanel.SearchText = "pets";

        Assert.True(vm.CollectionsPanel.IsSearchActive);
        Assert.Single(vm.CollectionsPanel.SearchResults);
        Assert.Equal("List pets", vm.CollectionsPanel.SearchResults[0].Name);
    }

    [Fact]
    public void SearchText_Empty_ClearsResultsAndDeactivatesSearch()
    {
        var (vm, _) = CreateVm();
        vm.CollectionsPanel.SearchText = "pets";

        vm.CollectionsPanel.SearchText = "";

        Assert.False(vm.CollectionsPanel.IsSearchActive);
        Assert.Empty(vm.CollectionsPanel.SearchResults);
    }

    [Fact]
    public void VisibilityFlags_FollowCollectionsPanelSearchAndHistoryToggle()
    {
        var (vm, _) = CreateVm();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.CollectionsPanel.SearchText = "pets";

        Assert.Contains(nameof(MainWindowViewModel.IsCollectionsTreeVisible), changed);
        Assert.Contains(nameof(MainWindowViewModel.IsSearchResultsVisible), changed);
        Assert.False(vm.IsCollectionsTreeVisible);
        Assert.True(vm.IsSearchResultsVisible);

        vm.ShowHistoryCommand.Execute(null);
        Assert.False(vm.IsCollectionsTreeVisible);
        Assert.False(vm.IsSearchResultsVisible);

        vm.ShowCollectionsCommand.Execute(null);
        vm.CollectionsPanel.SearchText = "";
        Assert.True(vm.IsCollectionsTreeVisible);
        Assert.False(vm.IsSearchResultsVisible);
    }

    [Fact]
    public async Task Send_RecordsHistoryEntryAndRefreshesHistoryViewModel()
    {
        var service = new FakeWorkspaceService { WorkspaceToReturn = new Workspace() };
        var executionService = new FakeExecutionService();
        var historyStore = new FakeHistoryStore();
        var vm = new MainWindowViewModel(service, executionService, null, historyStore);
        vm.AddTabCommand.Execute(null);
        vm.ActiveTab!.RequestUrl = "https://example.test/pets";

        await vm.ActiveTab.SendCommand.ExecuteAsync(null);

        Assert.Single(historyStore.Entries);
        Assert.Single(vm.History.Entries);
        Assert.Equal("https://example.test/pets", vm.History.Entries[0].RequestSnapshot.Url);
    }

    [Fact]
    public async Task ResendFromHistory_OpensNewTabThatRoundTripsSnapshotAndResolvesAgainstLiveEnvironment()
    {
        var service = new FakeWorkspaceService { WorkspaceToReturn = new Workspace() };
        var executionService = new FakeExecutionService();
        var historyStore = new FakeHistoryStore();
        var vm = new MainWindowViewModel(service, executionService, null, historyStore);
        var devEnvironment = new RequestEnvironment { Name = "Dev", Variables = { new EnvironmentVariable { Key = "host", InitialValue = new EncryptedString("dev.example.test") } } };
        vm.Environments.Add(devEnvironment);
        vm.SelectedEnvironment = devEnvironment;

        vm.AddTabCommand.Execute(null);
        vm.ActiveTab!.RequestUrl = "https://{{host}}/pets";
        vm.ActiveTab.Name = "List pets";
        await vm.ActiveTab.SendCommand.ExecuteAsync(null);
        var recordedEntry = historyStore.Entries[0];
        var tabCountBeforeResend = vm.OpenRequests.Count;

        vm.History.ResendCommand.Execute(recordedEntry);

        Assert.Equal(tabCountBeforeResend + 1, vm.OpenRequests.Count);
        Assert.Same(vm.OpenRequests[^1], vm.ActiveTab);
        // The resent tab reconstructs the pre-resolution template exactly...
        Assert.Equal("https://{{host}}/pets", vm.ActiveTab.RequestUrl);
        Assert.Equal("List pets", vm.ActiveTab.Name);

        // ...and resolves against whichever environment is selected now, not at capture time.
        var stagingEnvironment = new RequestEnvironment { Name = "Staging", Variables = { new EnvironmentVariable { Key = "host", InitialValue = new EncryptedString("staging.example.test") } } };
        vm.Environments.Add(stagingEnvironment);
        vm.SelectedEnvironment = stagingEnvironment;

        await vm.ActiveTab.SendCommand.ExecuteAsync(null);

        Assert.NotNull(executionService.LastExecutionOptions?.Variables);
        Assert.Equal("staging.example.test", executionService.LastExecutionOptions!.Variables!["host"]);
    }

    [Fact]
    public void ResendFromHistory_TruncatedBody_OpensTabWithIncompleteBodyNotice()
    {
        var service = new FakeWorkspaceService { WorkspaceToReturn = new Workspace() };
        var vm = new MainWindowViewModel(service, new FakeExecutionService(), null, new FakeHistoryStore());
        var entry = new HistoryEntry
        {
            RequestSnapshot = new RequestItem { Name = "Upload", Url = "https://example.test/upload", Body = "partial" },
            RequestBodyWasTruncatedForHistory = true
        };

        vm.History.ResendCommand.Execute(entry);

        Assert.Equal("partial", vm.ActiveTab!.RequestBody);
        Assert.Equal(string.Format(ApiConnectTool.Services.Localization.AppLocalization.Get("Str.History.RequestBodyTruncated"), 64), vm.ActiveTab.RequestBodyIncompleteNotice);
        Assert.False(string.IsNullOrWhiteSpace(vm.ActiveTab.RequestBodyIncompleteNotice));
        Assert.NotEqual("Str.History.RequestBodyTruncated", vm.ActiveTab.RequestBodyIncompleteNotice);
    }

    [Fact]
    public void ResendFromHistory_TruncatedStaleBodyInFormDataMode_ShowsNoticeOnlyOnceSwitchedToRaw()
    {
        var service = new FakeWorkspaceService { WorkspaceToReturn = new Workspace() };
        var vm = new MainWindowViewModel(service, new FakeExecutionService(), null, new FakeHistoryStore());
        var entry = new HistoryEntry
        {
            RequestSnapshot = new RequestItem { Name = "Form", BodyMode = RequestBodyMode.FormData, Body = "stale raw text" },
            RequestBodyWasTruncatedForHistory = true
        };

        vm.History.ResendCommand.Execute(entry);
        var tab = vm.ActiveTab!;
        Assert.Null(tab.RequestBodyIncompleteNotice);

        var notified = false;
        tab.PropertyChanged += (_, args) => notified |= args.PropertyName == nameof(RequestTabViewModel.RequestBodyIncompleteNotice);
        tab.SelectedBodyModeIndex = (int)RequestBodyMode.Raw;

        Assert.True(notified);
        Assert.NotNull(tab.RequestBodyIncompleteNotice);
    }

    [Fact]
    public void ResendFromHistory_CompleteBody_OpensTabWithoutNotice()
    {
        var service = new FakeWorkspaceService { WorkspaceToReturn = new Workspace() };
        var vm = new MainWindowViewModel(service, new FakeExecutionService(), null, new FakeHistoryStore());
        var entry = new HistoryEntry { RequestSnapshot = new RequestItem { Name = "Small", Body = "whole" } };

        vm.History.ResendCommand.Execute(entry);

        Assert.Null(vm.ActiveTab!.RequestBodyIncompleteNotice);
    }

    [Fact]
    public void OpeningANewTab_HasNoIncompleteBodyNotice()
    {
        var (vm, _) = CreateVm();

        vm.AddTabCommand.Execute(null);

        Assert.Null(vm.ActiveTab!.RequestBodyIncompleteNotice);
    }

    [Fact]
    public void TryCompareHistoryEntries_FirstCall_ReturnsNullAndPicksTheEntry()
    {
        var (vm, _) = CreateVm();
        var entry = new HistoryEntry { RequestSnapshot = new RequestItem { Name = "A" } };

        var result = vm.TryCompareHistoryEntries(entry);

        Assert.Null(result);
        Assert.True(vm.HasPendingCompare);
    }

    [Fact]
    public void TryCompareHistoryEntries_SecondDistinctEntry_ReturnsComparisonAndResetsPicker()
    {
        var (vm, _) = CreateVm();
        var first = new HistoryEntry { RequestSnapshot = new RequestItem { Name = "A" }, RedactedResponseBodyPreview = "old" };
        var second = new HistoryEntry { RequestSnapshot = new RequestItem { Name = "B" }, RedactedResponseBodyPreview = "new" };
        vm.TryCompareHistoryEntries(first);

        var result = vm.TryCompareHistoryEntries(second);

        Assert.NotNull(result);
        Assert.False(vm.HasPendingCompare);
        Assert.Equal("old", result!.Left.BodyText);
        Assert.Equal("new", result.Right.BodyText);
        var bodyDiff = result.BodyDiff;
        Assert.Contains(bodyDiff, l => l.Kind == ApiConnectTool.Services.DiffLineKind.Removed && l.Text == "old");
        Assert.Contains(bodyDiff, l => l.Kind == ApiConnectTool.Services.DiffLineKind.Added && l.Text == "new");
    }

    [Fact]
    public void CompareHistoryEntryWithCurrentTab_NoActiveTab_ReturnsNull()
    {
        var (vm, _) = CreateVm();
        var entry = new HistoryEntry { RequestSnapshot = new RequestItem { Name = "A" } };

        var result = vm.CompareHistoryEntryWithCurrentTab(entry);

        Assert.Null(result);
    }

    [Fact]
    public void CompareHistoryEntryWithCurrentTab_WithActiveTab_BuildsComparisonAgainstLiveResponse()
    {
        var (vm, _) = CreateVm();
        vm.AddTabCommand.Execute(null);
        var entry = new HistoryEntry { RequestSnapshot = new RequestItem { Name = "Historical" }, RedactedResponseBodyPreview = "captured" };

        var result = vm.CompareHistoryEntryWithCurrentTab(entry);

        Assert.NotNull(result);
        Assert.Equal("captured", result!.Right.BodyText);
    }

    [Fact]
    public void ExportCollection_ReturnsExportServiceOutputForTheGivenCollection()
    {
        var (vm, _) = CreateVm();
        vm.CollectionsPanel.AddCollectionCommand.Execute(null);
        var collection = vm.CollectionsPanel.Collections[0];

        var json = vm.CollectionsPanel.ExportCollection(collection, includeSecrets: false);

        Assert.Contains("\"ExportSchemaVersion\"", json);
        Assert.Contains(collection.Name, json);
    }

    [Fact]
    public void ImportCollection_AddsANewCollectionWithDisambiguatedNameAndPersists()
    {
        var (vm, service) = CreateVm();
        vm.CollectionsPanel.AddCollectionCommand.Execute(null); // "New Collection"
        var exported = vm.CollectionsPanel.ExportCollection(vm.CollectionsPanel.Collections[0], includeSecrets: false);
        var saveCountBefore = service.SaveCallCount;

        vm.CollectionsPanel.ImportCollection(exported);

        Assert.Equal(2, vm.CollectionsPanel.Collections.Count);
        Assert.NotEqual(vm.CollectionsPanel.Collections[0].Name, vm.CollectionsPanel.Collections[1].Name);
        Assert.NotEqual(vm.CollectionsPanel.Collections[0].Id, vm.CollectionsPanel.Collections[1].Id);
        Assert.True(service.SaveCallCount > saveCountBefore);
    }

    [Fact]
    public void ImportFromCurl_ValidCommand_OpensNewUnsavedTab()
    {
        var (vm, _) = CreateVm();

        vm.CollectionsPanel.ImportFromCurl("curl -X POST https://api.example.test/pets -H 'Accept: application/json' -d 'name=Mochi'");

        var tab = Assert.Single(vm.OpenRequests);
        Assert.Same(tab, vm.ActiveTab);
        // A freshly parsed request always gets a real (if unmatched) Id from RequestItem's own
        // constructor - it just doesn't correspond to anything already in Collections, so Save
        // still behaves like Save As (creates a new entry) rather than overwriting one.
        Assert.DoesNotContain(vm.CollectionsPanel.Collections.SelectMany(c => c.Requests), r => r.Id == tab.RequestId);
        Assert.Equal("https://api.example.test/pets", tab.RequestUrl);
        Assert.Equal("POST", tab.Method);
    }

    [Fact]
    public void ImportFromCurl_MalformedCommand_ThrowsAndOpensNoTab()
    {
        var (vm, _) = CreateVm();

        Assert.Throws<ApiConnectTool.Services.CurlImportException>(() => vm.CollectionsPanel.ImportFromCurl("not a curl command"));
        Assert.Empty(vm.OpenRequests);
    }

    [Fact]
    public void ExportCollectionAsPostman_ReturnsPostmanConverterOutput()
    {
        var (vm, _) = CreateVm();
        vm.CollectionsPanel.AddCollectionCommand.Execute(null);
        var collection = vm.CollectionsPanel.Collections[0];

        var json = vm.CollectionsPanel.ExportCollectionAsPostman(collection, includeSecrets: false);

        Assert.Contains("\"info\"", json);
        Assert.Contains(collection.Name, json);
    }

    [Fact]
    public void ImportPostmanCollection_AddsANewCollectionWithDisambiguatedNameAndPersists()
    {
        var (vm, service) = CreateVm();
        vm.CollectionsPanel.AddCollectionCommand.Execute(null); // "New Collection"
        var exported = vm.CollectionsPanel.ExportCollectionAsPostman(vm.CollectionsPanel.Collections[0], includeSecrets: false);
        var saveCountBefore = service.SaveCallCount;

        vm.CollectionsPanel.ImportPostmanCollection(exported);

        Assert.Equal(2, vm.CollectionsPanel.Collections.Count);
        Assert.NotEqual(vm.CollectionsPanel.Collections[0].Name, vm.CollectionsPanel.Collections[1].Name);
        Assert.True(service.SaveCallCount > saveCountBefore);
    }

    [Fact]
    public void ImportOpenApiCollection_AddsANewCollectionWithDisambiguatedNameAndPersists()
    {
        const string spec = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Pets API" },
              "paths": { "/pets": { "get": {} } }
            }
            """;
        var (vm, service) = CreateVm();
        vm.CollectionsPanel.AddCollectionCommand.Execute(null); // "New Collection" - unrelated to the imported name
        var saveCountBefore = service.SaveCallCount;

        vm.CollectionsPanel.ImportOpenApiCollection(spec);

        Assert.Equal(2, vm.CollectionsPanel.Collections.Count);
        Assert.Contains(vm.CollectionsPanel.Collections, c => c.Name == "Pets API");
        Assert.True(service.SaveCallCount > saveCountBefore);
    }

    [Fact]
    public void ImportOpenApiCollection_MalformedDocument_ThrowsAndAddsNoCollection()
    {
        var (vm, _) = CreateVm();

        Assert.Throws<ApiConnectTool.Services.OpenApiImportException>(() => vm.CollectionsPanel.ImportOpenApiCollection("not a spec"));
        Assert.Empty(vm.CollectionsPanel.Collections);
    }

    [Fact]
    public void SaveWorkspaceToDisk_UnrelatedEdit_PreservesTreeNodeIdentity()
    {
        var (vm, _) = CreateVm();
        vm.CollectionsPanel.AddCollectionCommand.Execute(null);
        var collection = vm.CollectionsPanel.Collections[0];
        vm.CollectionsPanel.AddFolderCommand.Execute(collection);
        vm.CollectionsPanel.AddRequestCommand.Execute(collection);
        var otherCollection = new RequestCollection { Name = "Other" };
        vm.CollectionsPanel.Collections.Add(otherCollection);
        vm.SaveWorkspaceToDisk();

        var collectionNode = vm.CollectionsPanel.Tree.Single(node => node.Collection == collection);
        var folderNode = collectionNode.Folders.Single();
        var requestInstance = collection.Requests.Single();

        vm.CollectionsPanel.RenameRequestCommand.Execute(new RequestItem { Name = "unrelated" });
        otherCollection.Name = "Renamed";
        vm.SaveWorkspaceToDisk();

        Assert.Same(collectionNode, vm.CollectionsPanel.Tree.Single(node => node.Collection == collection));
        Assert.Same(folderNode, vm.CollectionsPanel.Tree.Single(node => node.Collection == collection).Folders.Single());
        Assert.Same(requestInstance, collection.Requests.Single());
    }

    [Fact]
    public void CancelRenameCommand_RevertsNameAndClearsEditingFlag()
    {
        var (vm, _) = CreateVm();
        vm.CollectionsPanel.AddCollectionCommand.Execute(null);
        var collection = vm.CollectionsPanel.Collections[0];
        vm.CollectionsPanel.AddRequestCommand.Execute(collection);
        var request = collection.Requests.Single();
        request.RenameOriginalName = request.Name;
        request.IsEditing = true;
        request.Name = "typed but abandoned";

        vm.CollectionsPanel.CancelRenameCommand.Execute(request);

        Assert.False(request.IsEditing);
        Assert.NotEqual("typed but abandoned", request.Name);
    }

    [Fact]
    public void SaveWorkspaceToDisk_RenameViaCommand_DoesNotRestructureTree()
    {
        var (vm, _) = CreateVm();
        vm.CollectionsPanel.AddCollectionCommand.Execute(null);
        var collection = vm.CollectionsPanel.Collections[0];
        vm.CollectionsPanel.AddRequestCommand.Execute(collection);
        var request = collection.Requests.Single();
        var collectionNode = vm.CollectionsPanel.Tree.Single();

        request.Name = "Renamed request";
        vm.CollectionsPanel.RenameRequestCommand.Execute(request);

        Assert.Same(collectionNode, vm.CollectionsPanel.Tree.Single());
        Assert.Same(request, collectionNode.Requests.Single());
        Assert.Equal("Renamed request", collectionNode.Requests.Single().Name);
    }

    private sealed class FakeExecutionService : ApiConnectTool.Services.IRequestExecutionService
    {
        public RequestExecutionOptions? LastExecutionOptions { get; private set; }

        public Task<RequestExecutionResult> ExecuteAsync(RequestItem request, RequestExecutionOptions? options = null, CancellationToken cancellationToken = default)
        {
            LastExecutionOptions = options;
            return Task.FromResult(new RequestExecutionResult { IsSuccess = true, StatusCode = System.Net.HttpStatusCode.OK, ContentType = "text/plain", Body = "ok"u8.ToArray() });
        }

        public Task<RequestDownloadResult> DownloadAsync(RequestItem request, string destinationPath, RequestExecutionOptions? options = null, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RequestDownloadResult { IsSuccess = true });
    }

    private sealed class FakeHistoryStore : ApiConnectTool.Services.IHistoryStore
    {
        public List<HistoryEntry> Entries { get; } = new();
        public void Add(HistoryEntry entry) => Entries.Insert(0, entry);
        public IReadOnlyList<HistoryEntry> GetAll() => Entries;
    }
}
