using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class SessionRestoreViewModelTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "ApiConnectToolSessionVmTests_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private static (Workspace workspace, RequestItem first, RequestItem second, RequestItem third) CreateWorkspaceWithThreeRequests()
    {
        var first = new RequestItem
        {
            Id = Guid.NewGuid(),
            Name = "First",
            Url = "https://api.example.test/one",
            Auth = new RequestAuth { Type = AuthType.Bearer, Token = "super-secret-token" }
        };
        var second = new RequestItem
        {
            Id = Guid.NewGuid(),
            Name = "Second",
            Url = "https://api.example.test/two"
        };
        var third = new RequestItem
        {
            Id = Guid.NewGuid(),
            Name = "Third",
            Url = "https://api.example.test/three"
        };
        var workspace = new Workspace
        {
            Collections = new ObservableCollection<RequestCollection>
            {
                new()
                {
                    Name = "API",
                    Requests = new ObservableCollection<RequestItem> { first, second, third }
                }
            }
        };
        return (workspace, first, second, third);
    }

    [Fact]
    public void Startup_ReopensTabsInOrderWithSameActiveTab()
    {
        var (workspace, first, second, third) = CreateWorkspaceWithThreeRequests();
        var workspaceService = new FakeWorkspaceService { WorkspaceToReturn = workspace };
        var sessionStore = new SessionStore(_tempDirectory);
        sessionStore.Save(new SessionState([third.Id, first.Id, second.Id], first.Id));

        var vm = new MainWindowViewModel(workspaceService, sessionStore: sessionStore);

        Assert.Equal([third.Id, first.Id, second.Id], vm.OpenRequests.Select(tab => tab.RequestId!.Value));
        Assert.Equal(first.Id, vm.ActiveTab?.RequestId);
        Assert.Null(vm.WorkspaceWarning);
    }

    [Fact]
    public void Startup_SkipsDeletedRequestIdsWithoutErrorAndSelectsRemainingTab()
    {
        var (workspace, first, _, third) = CreateWorkspaceWithThreeRequests();
        var deletedId = Guid.NewGuid();
        var workspaceService = new FakeWorkspaceService { WorkspaceToReturn = workspace };
        var sessionStore = new SessionStore(_tempDirectory);
        sessionStore.Save(new SessionState([deletedId, third.Id, first.Id], deletedId));

        var vm = new MainWindowViewModel(workspaceService, sessionStore: sessionStore);

        Assert.Equal([third.Id, first.Id], vm.OpenRequests.Select(tab => tab.RequestId!.Value));
        Assert.Equal(third.Id, vm.ActiveTab?.RequestId);
        Assert.Null(vm.WorkspaceWarning);
    }

    [Fact]
    public void Startup_WithMissingOrCorruptSessionFile_OpensNoTabsAndShowsNoWarning()
    {
        var (workspace, _, _, _) = CreateWorkspaceWithThreeRequests();
        var workspaceService = new FakeWorkspaceService { WorkspaceToReturn = workspace };
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllText(Path.Combine(_tempDirectory, "session.json"), "{corrupt-json");
        var sessionStore = new SessionStore(_tempDirectory);

        var vm = new MainWindowViewModel(workspaceService, sessionStore: sessionStore);

        Assert.Empty(vm.OpenRequests);
        Assert.Null(vm.ActiveTab);
        Assert.Null(vm.WorkspaceWarning);
    }

    [Fact]
    public async Task OpeningClosingReorderingAndSwitchingTabs_UpdatesSessionFileAndOmitsUnsavedTabsAndSecrets()
    {
        var (workspace, first, second, third) = CreateWorkspaceWithThreeRequests();
        var workspaceService = new FakeWorkspaceService { WorkspaceToReturn = workspace };
        var sessionStore = new SessionStore(_tempDirectory);
        var vm = new MainWindowViewModel(workspaceService, sessionStore: sessionStore);

        // Opening saved tabs updates session.json
        vm.OpenRequestCommand.Execute(first);
        vm.OpenRequestCommand.Execute(second);
        vm.OpenRequestCommand.Execute(third);
        var afterOpen = sessionStore.Load();
        Assert.Equal([first.Id, second.Id, third.Id], afterOpen.OpenRequestIds);
        Assert.Equal(third.Id, afterOpen.ActiveRequestId);

        // Unsaved tabs are not recorded in session.json
        vm.AddTabCommand.Execute(null);
        vm.ActiveTab!.RequestUrl = "https://api.example.test/unsaved-draft";
        var withUnsaved = sessionStore.Load();
        Assert.Equal([first.Id, second.Id, third.Id], withUnsaved.OpenRequestIds);
        Assert.Null(withUnsaved.ActiveRequestId);

        // Switching to a saved tab updates ActiveRequestId
        vm.ActiveTab = vm.OpenRequests.First(tab => tab.RequestId == first.Id);
        var afterSwitch = sessionStore.Load();
        Assert.Equal([first.Id, second.Id, third.Id], afterSwitch.OpenRequestIds);
        Assert.Equal(first.Id, afterSwitch.ActiveRequestId);

        // Reordering tabs updates OpenRequestIds order
        vm.OpenRequests.Move(0, 2);
        var afterReorder = sessionStore.Load();
        Assert.Equal([second.Id, third.Id, first.Id], afterReorder.OpenRequestIds);
        Assert.Equal(first.Id, afterReorder.ActiveRequestId);

        // Closing a saved tab updates OpenRequestIds and ActiveRequestId
        await vm.CloseTabCommand.ExecuteAsync(vm.OpenRequests.First(tab => tab.RequestId == second.Id));
        var afterClose = sessionStore.Load();
        Assert.Equal([third.Id, first.Id], afterClose.OpenRequestIds);
        Assert.Equal(third.Id, afterClose.ActiveRequestId);

        // Verify session.json on disk contains only IDs and never URLs or secrets
        var rawSessionJson = File.ReadAllText(Path.Combine(_tempDirectory, "session.json"));
        Assert.DoesNotContain("super-secret-token", rawSessionJson);
        Assert.DoesNotContain("api.example.test", rawSessionJson);
        Assert.DoesNotContain("First", rawSessionJson);

        // Restarting a new MainWindowViewModel against the same store restores only the saved tabs in their new order
        var restarted = new MainWindowViewModel(workspaceService, sessionStore: sessionStore);
        Assert.Equal([third.Id, first.Id], restarted.OpenRequests.Select(tab => tab.RequestId!.Value));
        Assert.Equal(third.Id, restarted.ActiveTab?.RequestId);
    }

    [Fact]
    public async Task SavingNewTab_RecordsNewlySavedRequestIdInSession()
    {
        var (workspace, _, _, _) = CreateWorkspaceWithThreeRequests();
        var workspaceService = new FakeWorkspaceService { WorkspaceToReturn = workspace };
        var sessionStore = new SessionStore(_tempDirectory);
        var vm = new MainWindowViewModel(workspaceService, sessionStore: sessionStore);

        vm.AddTabCommand.Execute(null);
        vm.ActiveTab!.Name = "Newly Saved";
        await vm.SaveCommand.ExecuteAsync(null);

        var savedId = vm.ActiveTab.RequestId!.Value;
        var loaded = sessionStore.Load();
        Assert.Equal([savedId], loaded.OpenRequestIds);
        Assert.Equal(savedId, loaded.ActiveRequestId);
    }
}
