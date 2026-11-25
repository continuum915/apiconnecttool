using System.Collections.ObjectModel;
using System.Linq;
using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class CollectionEnvironmentPickerTests
{
    private readonly RequestEnvironment _ordersDev = new() { Name = "dev" };
    private readonly RequestEnvironment _ordersProd = new() { Name = "prod", IsProduction = true };
    private readonly RequestEnvironment _billingDev = new() { Name = "dev" };
    private readonly RequestEnvironment _billingQa = new() { Name = "qa" };
    private readonly RequestCollection _orders;
    private readonly RequestCollection _billing;
    private readonly RequestCollection _plain = new() { Name = "Plain" };
    private readonly RequestItem _ordersRequest = new() { Name = "List orders", Url = "https://orders.test" };
    private readonly RequestItem _billingRequest = new() { Name = "List invoices", Url = "https://billing.test" };
    private readonly FakeWorkspaceService _service;
    private readonly MainWindowViewModel _vm;

    public CollectionEnvironmentPickerTests()
    {
        _orders = new RequestCollection { Name = "Orders", Environments = { _ordersDev, _ordersProd }, ActiveEnvironmentId = _ordersDev.Id, Requests = { _ordersRequest } };
        _billing = new RequestCollection { Name = "Billing", Environments = { _billingDev, _billingQa }, ActiveEnvironmentId = _billingQa.Id, Requests = { _billingRequest } };
        _service = new FakeWorkspaceService
        {
            WorkspaceToReturn = new Workspace { Collections = new ObservableCollection<RequestCollection> { _orders, _billing, _plain } }
        };
        _vm = new MainWindowViewModel(_service);
    }

    private CollectionEnvironmentPickerViewModel Picker => _vm.CollectionEnvironmentPicker;

    private CollectionTreeNode Node(RequestCollection collection) => _vm.CollectionsPanel.Tree.Single(node => node.Collection == collection);

    [Fact]
    public void CollectionTab_ListsNoEnvironmentThenTheCollectionsEnvironments()
    {
        _vm.OpenRequestCommand.Execute(_ordersRequest);

        Assert.True(Picker.IsEnabled);
        Assert.Same(_orders, Picker.Collection);
        Assert.Equal(new[] { "No environment", "dev", "prod" }, Picker.Choices.Select(choice => choice.Name));
        Assert.Null(Picker.Choices[0].Environment);
        Assert.True(Picker.Choices[2].IsProduction);
        Assert.Same(_ordersDev, Picker.SelectedChoice!.Environment);
        Assert.Contains("Orders", Picker.ToolTip);
    }

    [Fact]
    public void DraftTab_DisablesThePickerAndExplainsWhy()
    {
        _vm.AddTabCommand.Execute(null);

        Assert.False(Picker.IsEnabled);
        Assert.Null(Picker.Collection);
        Assert.Empty(Picker.Choices);
        Assert.Null(Picker.SelectedChoice);
        Assert.Contains("global environment", Picker.ToolTip);
    }

    [Fact]
    public void NoOpenTab_DisablesThePicker()
    {
        Assert.Null(_vm.ActiveTab);
        Assert.False(Picker.IsEnabled);
    }

    [Fact]
    public void SwitchingTabs_ShowsTheOtherCollectionsEnvironments()
    {
        _vm.OpenRequestCommand.Execute(_ordersRequest);
        _vm.OpenRequestCommand.Execute(_billingRequest);

        Assert.Equal(new[] { "No environment", "dev", "qa" }, Picker.Choices.Select(choice => choice.Name));
        Assert.Same(_billingQa, Picker.SelectedChoice!.Environment);
    }

    [Fact]
    public void Selecting_SetsOnlyThatCollectionsActiveEnvironmentAndSaves()
    {
        _vm.OpenRequestCommand.Execute(_ordersRequest);
        var saves = _service.SaveCallCount;

        Picker.SelectedChoice = Picker.Choices.Single(choice => choice.Environment == _ordersProd);

        Assert.Equal(_ordersProd.Id, _orders.ActiveEnvironmentId);
        Assert.Equal(_billingQa.Id, _billing.ActiveEnvironmentId);
        Assert.Null(_plain.ActiveEnvironmentId);
        Assert.True(_service.SaveCallCount > saves);
        Assert.Same(_ordersProd, Picker.SelectedChoice!.Environment);
    }

    [Fact]
    public void SelectingNoEnvironment_ClearsTheActiveEnvironment()
    {
        _vm.OpenRequestCommand.Execute(_ordersRequest);

        Picker.SelectedChoice = Picker.Choices[0];

        Assert.Null(_orders.ActiveEnvironmentId);
        Assert.Equal(_billingQa.Id, _billing.ActiveEnvironmentId);
    }

    [Fact]
    public void StaleActiveEnvironment_ShowsNoEnvironmentWithoutSaving()
    {
        _orders.ActiveEnvironmentId = System.Guid.NewGuid();
        var saves = _service.SaveCallCount;

        _vm.OpenRequestCommand.Execute(_ordersRequest);

        Assert.Null(Picker.SelectedChoice!.Environment);
        Assert.Equal(saves, _service.SaveCallCount);
    }

    [Fact]
    public void Sidebar_ShowsEachCollectionsActiveEnvironment()
    {
        Assert.Equal("dev", Node(_orders).ActiveEnvironmentLabel);
        Assert.False(Node(_orders).IsActiveEnvironmentProduction);
        Assert.Equal("qa", Node(_billing).ActiveEnvironmentLabel);
        Assert.Null(Node(_plain).ActiveEnvironmentLabel);
    }

    [Fact]
    public void Sidebar_MarksProductionAndFollowsThePicker()
    {
        _vm.OpenRequestCommand.Execute(_ordersRequest);

        Picker.SelectedChoice = Picker.Choices.Single(choice => choice.Environment == _ordersProd);

        Assert.Equal("prod", Node(_orders).ActiveEnvironmentLabel);
        Assert.True(Node(_orders).IsActiveEnvironmentProduction);
        Assert.True(Node(_orders).IsActiveEnvironmentProduction);
        Assert.Equal("qa", Node(_billing).ActiveEnvironmentLabel);

        Picker.SelectedChoice = Picker.Choices[0];

        Assert.Equal("No environment", Node(_orders).ActiveEnvironmentLabel);
        Assert.False(Node(_orders).IsActiveEnvironmentProduction);
    }

    [Fact]
    public void GlobalPicker_IsShownOnlyWhenAGlobalEnvironmentExists()
    {
        Assert.False(_vm.HasGlobalEnvironments);

        _vm.Environments.Add(new RequestEnvironment { Name = "shared" });
        Assert.True(_vm.HasGlobalEnvironments);

        _vm.Environments.Clear();
        Assert.False(_vm.HasGlobalEnvironments);
    }
}
