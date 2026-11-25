using System.Collections.ObjectModel;
using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class EnvironmentEditorScopeTests
{
    private readonly RequestEnvironment _dev = new() { Name = "dev" };
    private readonly RequestEnvironment _qa = new() { Name = "qa" };
    private readonly RequestEnvironment _billingDev = new() { Name = "billing dev" };
    private readonly RequestEnvironment _shared = new() { Name = "shared" };
    private readonly RequestCollection _orders;
    private readonly RequestCollection _billing;
    private readonly MainWindowViewModel _vm;
    private readonly FakeWorkspaceService _service;

    public EnvironmentEditorScopeTests()
    {
        _orders = new RequestCollection { Name = "Orders", Environments = { _dev, _qa }, ActiveEnvironmentId = _dev.Id,
            Variables = new() { new() { Key = "version", InitialValue = "v1" } } };
        _billing = new RequestCollection { Name = "Billing", Environments = { _billingDev } };
        _service = new FakeWorkspaceService
        {
            WorkspaceToReturn = new Workspace
            {
                Collections = new ObservableCollection<RequestCollection> { _orders, _billing },
                Environments = new ObservableCollection<RequestEnvironment> { _shared }
            }
        };
        _vm = new MainWindowViewModel(_service);
    }

    [Fact]
    public void ScopeSelector_ListsEachCollectionThenTheGlobalScope_AndSwitches()
    {
        var editor = _vm.CreateEnvironmentEditor(_orders);
        Assert.Equal(new[] { "Orders", "Billing", "Global environments" }, editor.Scopes.Select(scope => scope.Name));
        Assert.Same(_orders, editor.SelectedScope!.Collection);
        Assert.Equal("Environments — Orders", editor.Title);

        editor.SelectedScope = editor.Scopes[1];
        Assert.Same(_billing, editor.Collection);
        Assert.Same(_billing.Environments, editor.Environments);
        Assert.Equal(new[] { "Collection variables", "billing dev" }, editor.DisplayEnvironments.Select(environment => environment.Name));
        Assert.Same(_billing.Variables, editor.CollectionVariables!.Variables);
        Assert.Equal("Environments — Billing", editor.Title);

        editor.SelectedScope = editor.Scopes[2];
        Assert.True(editor.IsGlobalScope);
        Assert.NotNull(editor.Globals);
        Assert.Null(editor.CollectionVariables);
        Assert.Equal(new[] { "Globals", "shared" }, editor.DisplayEnvironments.Select(environment => environment.Name));
        Assert.Equal("Environments — Global environments", editor.Title);
    }

    [Fact]
    public void CollectionVariables_EditsTheCollectionsVariables()
    {
        var editor = _vm.CreateEnvironmentEditor(_orders);
        editor.SelectedEnvironment = editor.CollectionVariables;
        Assert.False(editor.CanEditEnvironment);
        Assert.True(editor.IsScopeVariablesSelected);
        Assert.False(editor.SetActiveCommand.CanExecute(null));
        Assert.Contains("Collection Settings", editor.ScopeVariablesNote);

        editor.AddVariableCommand.Execute(null);
        Assert.Equal(2, _orders.Variables.Count);
        editor.VariableEditor!.Variables[1].Key = "region";
        Assert.Equal("region", _orders.Variables[1].Key);
        Assert.Same(_orders.Variables, _service.LastSaved!.Collections[0].Variables);
    }

    [Fact]
    public void SetAsActive_SetsTheCollectionsActiveEnvironment()
    {
        var editor = _vm.CreateEnvironmentEditor(_orders);
        Assert.Equal("Active for Orders: dev", editor.ActiveSummary);
        editor.SelectedEnvironment = _qa;
        Assert.True(editor.SetActiveCommand.CanExecute(null));

        editor.SetActiveCommand.Execute(null);
        Assert.Equal(_qa.Id, _orders.ActiveEnvironmentId);
        Assert.Equal("Active for Orders: qa", editor.ActiveSummary);
        Assert.False(editor.SetActiveCommand.CanExecute(null));
        Assert.Equal(_qa.Id, _service.LastSaved!.Collections[0].ActiveEnvironmentId);
    }

    [Fact]
    public void SetAsActive_InTheGlobalScope_SetsTheActiveGlobalEnvironment()
    {
        var editor = _vm.CreateEnvironmentEditor();
        Assert.Equal("Active for Global environments: No environment", editor.ActiveSummary);
        editor.SelectedEnvironment = _shared;
        editor.SetActiveCommand.Execute(null);
        Assert.Same(_shared, _vm.SelectedEnvironment);
        Assert.Equal("Active for Global environments: shared", editor.ActiveSummary);

        editor.DeleteEnvironmentCommand.Execute(_shared);
        Assert.Null(_vm.SelectedEnvironment);
    }

    [Fact]
    public void DeletingTheActiveCollectionEnvironment_ClearsIt()
    {
        var editor = _vm.CreateEnvironmentEditor(_orders);
        editor.DeleteEnvironmentCommand.Execute(_dev);
        Assert.Null(_orders.ActiveEnvironmentId);
        Assert.Same(_qa, editor.SelectedEnvironment);
    }

    [Fact]
    public void FirstListedEnvironment_IsTheFirstRealEnvironment()
    {
        var editor = _vm.CreateEnvironmentEditor(_orders);
        Assert.False(editor.IsFirstListedEnvironment(editor.CollectionVariables));
        Assert.True(editor.IsFirstListedEnvironment(_dev));
        Assert.False(editor.IsFirstListedEnvironment(_qa));
    }
}
