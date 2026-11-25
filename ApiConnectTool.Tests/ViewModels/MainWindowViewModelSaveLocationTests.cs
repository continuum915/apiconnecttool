using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

/// <summary>Choosing where a request is saved (first save, Save As) and preserving placement
/// when saving an open tab after a move.</summary>
public class MainWindowViewModelSaveLocationTests
{
    private readonly RequestCollection _api = new() { Name = "My API" };
    private readonly RequestCollection _auth = new() { Name = "Auth" };
    private readonly CollectionFolder _users = new() { Name = "Users", SortOrder = 0 };
    private readonly CollectionFolder _admin;
    private readonly CollectionFolder _orders = new() { Name = "Orders", SortOrder = 1 };
    private readonly FakeWorkspaceService _service;
    private readonly MainWindowViewModel _vm;

    public MainWindowViewModelSaveLocationTests()
    {
        _admin = new CollectionFolder { Name = "Admin", ParentFolderId = _users.Id };
        _api.Folders.Add(_orders);
        _api.Folders.Add(_users);
        _api.Folders.Add(_admin);
        _service = new FakeWorkspaceService
        {
            WorkspaceToReturn = new Workspace { Collections = new ObservableCollection<RequestCollection> { _api, _auth } }
        };
        _vm = new MainWindowViewModel(_service);
    }

    private RequestItem AddRequest(RequestCollection collection, string name, Guid? folderId = null)
    {
        var request = new RequestItem
        {
            Name = name,
            Url = $"https://api.example.test/{name}",
            FolderId = folderId,
            SortOrder = collection.Requests.Count(r => r.FolderId == folderId)
        };
        collection.Requests.Add(request);
        return request;
    }

    private static List<string> Names(RequestCollection collection, Guid? folderId) =>
        collection.Requests.Where(r => r.FolderId == folderId).OrderBy(r => r.SortOrder).Select(r => r.Name).ToList();

    private SaveDestination Destination(string path) =>
        _vm.CollectionsPanel.GetSaveDestinations().Single(destination => destination.DisplayPath == path);

    [Fact]
    public async Task Save_NewTab_AsksForNameAndLocation_AndSavesIntoTheChosenFolder()
    {
        SaveAsPromptRequest? asked = null;
        _vm.SaveAsPromptHandler = request =>
        {
            asked = request;
            return Task.FromResult<SaveAsPromptResult?>(new SaveAsPromptResult("List admins", false, Destination("My API / Users / Admin")));
        };
        _vm.AddTabCommand.Execute(null);
        _vm.ActiveTab!.Name = "Draft";
        _vm.ActiveTab.RequestUrl = "https://api.example.test/admins";

        await _vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(SaveAsPromptMode.SaveNew, asked!.Mode);
        Assert.Equal("Draft", asked.DefaultName);
        Assert.Equal(5, asked.Destinations.Count);
        var saved = Assert.Single(_api.Requests);
        Assert.Equal("List admins", saved.Name);
        Assert.Equal(_admin.Id, saved.FolderId);
        var adminNode = _vm.CollectionsPanel.Tree.Single(node => node.Collection == _api)
            .Folders.Single(node => node.Folder == _users).Folders.Single(node => node.Folder == _admin);
        Assert.Same(saved, Assert.Single(adminNode.Requests));
        Assert.Equal(saved.Id, _vm.ActiveTab.RequestId);
        Assert.False(_vm.ActiveTab.IsDirty);
    }

    [Fact]
    public async Task Save_NewTab_Cancelled_SavesNothingAndStaysDirty()
    {
        _vm.SaveAsPromptHandler = _ => Task.FromResult<SaveAsPromptResult?>(null);
        _vm.AddTabCommand.Execute(null);
        _vm.ActiveTab!.RequestUrl = "https://api.example.test/draft";
        var savesBefore = _service.SaveCallCount;

        await _vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_api.Requests);
        Assert.Empty(_auth.Requests);
        Assert.True(_vm.ActiveTab.IsDirty);
        Assert.Null(_vm.ActiveTab.RequestId);
        Assert.Equal(savesBefore, _service.SaveCallCount);
    }

    [Fact]
    public async Task Save_NewTab_AlwaysAsks_EvenWhenSaveAsPromptIsTurnedOff()
    {
        _vm.SkipSaveAsConfirmation = true;
        var asked = false;
        _vm.SaveAsPromptHandler = request =>
        {
            asked = true;
            return Task.FromResult<SaveAsPromptResult?>(new SaveAsPromptResult(request.DefaultName, DontShowAgain: true, Destination("Auth")));
        };
        _vm.AddTabCommand.Execute(null);

        await _vm.SaveCommand.ExecuteAsync(null);

        Assert.True(asked);
        Assert.Single(_auth.Requests);
    }

    [Fact]
    public async Task Save_ExistingRequest_DoesNotAsk()
    {
        var request = AddRequest(_api, "Get user", _users.Id);
        var asked = false;
        _vm.SaveAsPromptHandler = _ => { asked = true; return Task.FromResult<SaveAsPromptResult?>(null); };
        _vm.OpenRequestCommand.Execute(request);
        _vm.ActiveTab!.RequestUrl = "https://api.example.test/users/2";

        await _vm.SaveCommand.ExecuteAsync(null);

        Assert.False(asked);
        Assert.Equal("https://api.example.test/users/2", Assert.Single(_api.Requests).Url);
        Assert.Equal(_users.Id, _api.Requests[0].FolderId);
    }

    [Fact]
    public async Task SaveAs_DefaultsToTheSourceRequestsFolder_AndCanSaveElsewhere()
    {
        var request = AddRequest(_api, "Get user", _users.Id);
        SaveAsPromptRequest? asked = null;
        _vm.SaveAsPromptHandler = prompt =>
        {
            asked = prompt;
            return Task.FromResult<SaveAsPromptResult?>(new SaveAsPromptResult("Get user (auth)", false, Destination("Auth")));
        };
        _vm.OpenRequestCommand.Execute(request);

        await _vm.SaveAsCommand.ExecuteAsync(null);

        Assert.Equal(SaveAsPromptMode.SaveAsCopy, asked!.Mode);
        Assert.Equal("My API / Users", asked.DefaultDestination!.DisplayPath);
        var copy = Assert.Single(_auth.Requests);
        Assert.Equal("Get user (auth)", copy.Name);
        Assert.Null(copy.FolderId);
        Assert.Equal(copy.Id, _vm.ActiveTab!.RequestId);
        Assert.Single(_api.Requests);
        Assert.Same(copy, Assert.Single(_vm.CollectionsPanel.Tree.Single(node => node.Collection == _auth).Requests));
    }

    [Fact]
    public async Task SaveAs_PromptTurnedOff_CopiesIntoTheSourceRequestsFolder()
    {
        var request = AddRequest(_api, "Get user", _users.Id);
        _vm.SkipSaveAsConfirmation = true;
        _vm.OpenRequestCommand.Execute(request);

        await _vm.SaveAsCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "Get user", "Copy of Get user" }, Names(_api, _users.Id));
    }

    [Fact]
    public async Task Duplicate_AsksInDuplicateMode_WithoutLocations()
    {
        var request = AddRequest(_api, "Get user", _users.Id);
        SaveAsPromptRequest? asked = null;
        _vm.SaveAsPromptHandler = prompt => { asked = prompt; return Task.FromResult<SaveAsPromptResult?>(new SaveAsPromptResult(prompt.DefaultName, false)); };

        await _vm.CollectionsPanel.DuplicateRequestCommand.ExecuteAsync(request);

        Assert.Equal(SaveAsPromptMode.Duplicate, asked!.Mode);
        Assert.Empty(asked.Destinations);
        Assert.Equal(new[] { "Get user", "Copy of Get user" }, Names(_api, _users.Id));
    }

    [Fact]
    public async Task CloseTab_NewDirtyTab_SaveAsksForLocation_CancellingKeepsTheTabOpen()
    {
        _vm.UnsavedChangesPromptHandler = _ => Task.FromResult(UnsavedChangesChoice.Save);
        _vm.SaveAsPromptHandler = _ => Task.FromResult<SaveAsPromptResult?>(null);
        _vm.AddTabCommand.Execute(null);
        var tab = _vm.ActiveTab!;
        tab.RequestUrl = "https://api.example.test/draft";

        await _vm.CloseTabCommand.ExecuteAsync(tab);

        Assert.Same(tab, Assert.Single(_vm.OpenRequests));
        Assert.True(tab.IsDirty);
    }

    [Fact]
    public async Task CloseTab_NewDirtyTab_SaveIntoChosenLocationThenCloses()
    {
        _vm.UnsavedChangesPromptHandler = _ => Task.FromResult(UnsavedChangesChoice.Save);
        _vm.SaveAsPromptHandler = prompt => Task.FromResult<SaveAsPromptResult?>(new SaveAsPromptResult("Draft", false, Destination("My API / Orders")));
        _vm.AddTabCommand.Execute(null);
        _vm.ActiveTab!.RequestUrl = "https://api.example.test/orders";

        await _vm.CloseTabCommand.ExecuteAsync(_vm.ActiveTab);

        Assert.Empty(_vm.OpenRequests);
        Assert.Equal(_orders.Id, Assert.Single(_api.Requests).FolderId);
    }

    [Fact]
    public async Task MovedRequest_OpenTab_KeepsItsNewLocationWhenSaved()
    {
        var request = AddRequest(_api, "Get user", _users.Id);
        _vm.OpenRequestCommand.Execute(request);
        _vm.CollectionsPanel.MoveDestinationPromptHandler = (_, destinations, _) =>
            Task.FromResult<SaveDestination?>(destinations.Single(d => d.DisplayPath == "Auth"));
        await _vm.CollectionsPanel.MoveRequestToCommand.ExecuteAsync(request);
        _vm.ActiveTab!.RequestUrl = "https://api.example.test/users/2";

        await _vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_api.Requests);
        var saved = Assert.Single(_auth.Requests);
        Assert.Null(saved.FolderId);
        Assert.Equal("https://api.example.test/users/2", saved.Url);
    }

}
