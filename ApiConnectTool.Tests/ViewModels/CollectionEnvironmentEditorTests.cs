using System;
using System.Collections.ObjectModel;
using System.Linq;
using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class CollectionEnvironmentEditorTests
{
    private readonly RequestEnvironment _ordersDev = new() { Name = "dev" };
    private readonly RequestEnvironment _ordersQa = new() { Name = "qa" };
    private readonly RequestEnvironment _sharedDev;
    private readonly RequestCollection _orders;
    private readonly RequestCollection _billing = new() { Name = "Billing" };
    private readonly Workspace _workspace;
    private readonly FakeWorkspaceService _service;
    private readonly MainWindowViewModel _vm;

    public CollectionEnvironmentEditorTests()
    {
        _sharedDev = new RequestEnvironment
        {
            Name = "shared dev",
            IsProduction = true,
            Variables =
            {
                new EnvironmentVariable { Key = "host", InitialValue = "https://dev.test" },
                new EnvironmentVariable { Key = "token", InitialValue = "secret-default", IsSecret = true },
                new EnvironmentVariable { Key = "user", InitialValue = "alice", CurrentValue = "bob", HasCurrentValue = true }
            }
        };
        _orders = new RequestCollection { Name = "Orders", Environments = { _ordersDev, _ordersQa }, ActiveEnvironmentId = _ordersQa.Id };
        _workspace = new Workspace
        {
            Collections = new ObservableCollection<RequestCollection> { _orders, _billing },
            Environments = new ObservableCollection<RequestEnvironment> { _sharedDev },
            SelectedEnvironmentId = _sharedDev.Id
        };
        _service = new FakeWorkspaceService { WorkspaceToReturn = _workspace };
        _vm = new MainWindowViewModel(_service);
    }

    [Fact]
    public void CollectionScope_EditsOnlyThatCollectionsEnvironments()
    {
        var editor = _vm.CreateEnvironmentEditor(_orders);

        Assert.Same(_orders, editor.Collection);
        Assert.False(editor.IsGlobalScope);
        Assert.Null(editor.Globals);
        Assert.Same(_orders.Environments, editor.Environments);
        Assert.Equal(new[] { "Collection variables", "dev", "qa" }, editor.DisplayEnvironments.Select(environment => environment.Name));
        Assert.Same(_orders.Variables, editor.CollectionVariables!.Variables);
        Assert.Same(_ordersQa, editor.SelectedEnvironment);
        Assert.Contains("Orders", editor.Title);
        Assert.False(editor.CanTransferToCollection);
        Assert.Null(editor.CreateTransfer(move: false));
    }

    [Fact]
    public void CollectionScope_AddAndDuplicateStayInTheCollection()
    {
        var editor = _vm.CreateEnvironmentEditor(_orders);
        var saves = _service.SaveCallCount;

        editor.AddEnvironmentCommand.Execute(null);
        var duplication = editor.CreateDuplication()!;
        Assert.True(duplication.TryDuplicate());

        Assert.Equal(4, _orders.Environments.Count);
        Assert.Single(_workspace.Environments);
        Assert.Empty(_billing.Environments);
        Assert.True(_service.SaveCallCount > saves);
        Assert.Equal(_orders.Environments.Count, _orders.Environments.Select(environment => environment.Id).Distinct().Count());
    }

    [Fact]
    public void CollectionScope_SearchFiltersTheCollectionsEnvironments()
    {
        var editor = _vm.CreateEnvironmentEditor(_orders);

        editor.SearchText = "q";

        Assert.Equal(new[] { "qa" }, editor.DisplayEnvironments.Select(environment => environment.Name));
    }

    [Fact]
    public void DeletingTheActiveCollectionEnvironment_ClearsTheActiveEnvironment()
    {
        var editor = _vm.CreateEnvironmentEditor(_orders);

        editor.DeleteEnvironmentCommand.Execute(_ordersQa);

        Assert.Equal(new[] { _ordersDev }, _orders.Environments);
        Assert.Null(_orders.ActiveEnvironmentId);
        Assert.Equal("No environment", _vm.CollectionsPanel.Tree.Single(node => node.Collection == _orders).ActiveEnvironmentLabel);
        Assert.Same(_sharedDev, _vm.SelectedEnvironment);
    }

    [Fact]
    public void DeletingAnotherCollectionEnvironment_KeepsTheActiveEnvironment()
    {
        var editor = _vm.CreateEnvironmentEditor(_orders);

        editor.DeleteEnvironmentCommand.Execute(_ordersDev);

        Assert.Equal(_ordersQa.Id, _orders.ActiveEnvironmentId);
    }

    [Fact]
    public void GlobalScope_IsUnchangedAndOffersTransfers()
    {
        var editor = _vm.CreateEnvironmentEditor();

        Assert.True(editor.IsGlobalScope);
        Assert.NotNull(editor.Globals);
        Assert.Same(_workspace.Environments, editor.Environments);
        editor.SelectedEnvironment = _sharedDev;
        Assert.True(editor.CanTransferToCollection);
        editor.SelectedEnvironment = editor.Globals;
        Assert.False(editor.CanTransferToCollection);
    }

    [Fact]
    public void Copy_AddsANewEnvironmentToTheTargetAndKeepsTheGlobalOne()
    {
        var editor = _vm.CreateEnvironmentEditor();
        editor.SelectedEnvironment = _sharedDev;
        var transfer = editor.CreateTransfer(move: false)!;
        transfer.TargetCollection = _billing;

        Assert.True(editor.ApplyTransfer(transfer));

        var copy = Assert.Single(_billing.Environments);
        Assert.NotEqual(_sharedDev.Id, copy.Id);
        Assert.Equal("shared dev", copy.Name);
        Assert.True(copy.IsProduction);
        Assert.Equal(new[] { _sharedDev }, _workspace.Environments);
        Assert.Null(_billing.ActiveEnvironmentId);
        Assert.Equal("https://dev.test", copy.Variables[0].InitialValue.Value);
        Assert.True(copy.Variables[1].IsSecret);
        Assert.Equal(string.Empty, copy.Variables[1].InitialValue.Value);
        Assert.Equal("alice", copy.Variables[2].InitialValue.Value);
        Assert.False(copy.Variables[2].HasCurrentValue);
        Assert.Same(_workspace, _service.LastSaved);
    }

    [Fact]
    public void Copy_WithSecretsAndOverrides_CopiesThem()
    {
        var editor = _vm.CreateEnvironmentEditor();
        editor.SelectedEnvironment = _sharedDev;
        var transfer = editor.CreateTransfer(move: false)!;
        transfer.IncludeSecretValues = true;
        transfer.CopyOverrides = true;

        Assert.True(editor.ApplyTransfer(transfer));

        var copy = Assert.Single(_orders.Environments, environment => environment.Name == "shared dev");
        Assert.Equal("secret-default", copy.Variables[1].InitialValue.Value);
        Assert.Equal("bob", copy.Variables[2].CurrentValue.Value);
        Assert.True(copy.Variables[2].HasCurrentValue);
    }

    [Fact]
    public void Move_DeletesTheGlobalOriginalAndClearsItsSelection()
    {
        var editor = _vm.CreateEnvironmentEditor();
        editor.SelectedEnvironment = _sharedDev;
        var transfer = editor.CreateTransfer(move: true)!;
        Assert.True(transfer.IsMove);
        transfer.TargetCollection = _billing;

        Assert.True(editor.ApplyTransfer(transfer));

        Assert.Empty(_workspace.Environments);
        Assert.Equal("shared dev", Assert.Single(_billing.Environments).Name);
        Assert.Null(_vm.SelectedEnvironment);
        Assert.Null(_workspace.SelectedEnvironmentId);
        Assert.False(_vm.HasGlobalEnvironments);
    }

    [Fact]
    public void Transfer_RejectsANameTakenInTheTargetCollection()
    {
        _billing.Environments.Add(new RequestEnvironment { Name = "SHARED DEV" });
        var editor = _vm.CreateEnvironmentEditor();
        editor.SelectedEnvironment = _sharedDev;
        var transfer = editor.CreateTransfer(move: false)!;

        Assert.Equal("shared dev", transfer.Name);
        transfer.TargetCollection = _billing;
        Assert.False(transfer.CanTransfer);
        Assert.False(editor.ApplyTransfer(transfer));
        Assert.Single(_billing.Environments);

        transfer.Name = "  ";
        Assert.False(transfer.Validate());

        transfer.Name = "dev from shared";
        Assert.True(editor.ApplyTransfer(transfer));
        Assert.Equal(2, _billing.Environments.Count);
    }

    [Fact]
    public void Transfer_SuggestsAFreeNameInTheFirstCollection()
    {
        _orders.Environments.Add(new RequestEnvironment { Name = "shared dev" });
        var editor = _vm.CreateEnvironmentEditor();
        editor.SelectedEnvironment = _sharedDev;

        var transfer = editor.CreateTransfer(move: false)!;

        Assert.Same(_orders, transfer.TargetCollection);
        Assert.Equal("shared dev 2", transfer.Name);
    }

    [Fact]
    public void Move_WithAnUndecryptableValue_ChangesNothing()
    {
        _sharedDev.Variables[1].InitialValue = EncryptedString.Undecryptable();
        var editor = _vm.CreateEnvironmentEditor();
        editor.SelectedEnvironment = _sharedDev;
        var transfer = editor.CreateTransfer(move: true)!;
        transfer.IncludeSecretValues = true;

        Assert.False(editor.ApplyTransfer(transfer));

        Assert.NotNull(transfer.Error);
        Assert.Equal(new[] { _sharedDev }, _workspace.Environments);
        Assert.Equal(2, _orders.Environments.Count);
    }

    [Fact]
    public void Transfer_IsUnavailableWithoutCollections()
    {
        _workspace.Collections.Clear();
        var editor = _vm.CreateEnvironmentEditor();
        editor.SelectedEnvironment = _sharedDev;

        Assert.False(editor.CanTransferToCollection);
        Assert.Null(editor.CreateTransfer(move: true));
    }
}
