using System.Collections.ObjectModel;
using System.Linq;
using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class UseInEveryCollectionTests
{
    private readonly RequestEnvironment _ordersDev = new() { Name = "dev" };
    private readonly RequestEnvironment _ordersQa = new() { Name = "qa" };
    private readonly RequestEnvironment _billingDev = new() { Name = "dev" };
    private readonly RequestEnvironment _billingQa = new() { Name = " QA " };
    private readonly RequestEnvironment _shippingProd = new() { Name = "prod" };
    private readonly RequestCollection _orders;
    private readonly RequestCollection _billing;
    private readonly RequestCollection _shipping;
    private readonly RequestCollection _plain = new() { Name = "Plain" };
    private readonly RequestItem _ordersRequest = new() { Name = "List orders" };
    private readonly FakeWorkspaceService _service;
    private readonly MainWindowViewModel _vm;

    public UseInEveryCollectionTests()
    {
        _orders = new RequestCollection { Name = "Orders", Environments = { _ordersDev, _ordersQa }, ActiveEnvironmentId = _ordersDev.Id, Requests = { _ordersRequest } };
        _billing = new RequestCollection { Name = "Billing", Environments = { _billingDev, _billingQa }, ActiveEnvironmentId = _billingDev.Id };
        _shipping = new RequestCollection { Name = "Shipping", Environments = { _shippingProd }, ActiveEnvironmentId = _shippingProd.Id };
        _service = new FakeWorkspaceService
        {
            WorkspaceToReturn = new Workspace { Collections = new ObservableCollection<RequestCollection> { _orders, _billing, _shipping, _plain } }
        };
        _vm = new MainWindowViewModel(_service);
        _vm.OpenRequestCommand.Execute(_ordersRequest);
    }

    private CollectionEnvironmentPickerViewModel Picker => _vm.CollectionEnvironmentPicker;

    private void Select(RequestEnvironment environment) =>
        Picker.SelectedChoice = Picker.Choices.Single(choice => choice.Environment == environment);

    [Fact]
    public void OffersTheSelectedEnvironmentsName()
    {
        Select(_ordersQa);

        Assert.Equal("Use 'qa' in every collection", Picker.UseInEveryCollectionText);
        Assert.True(Picker.UseInEveryCollectionCommand.CanExecute(null));
    }

    [Fact]
    public void IsUnavailableForNoEnvironmentAndDraftTabs()
    {
        Picker.SelectedChoice = Picker.Choices[0];
        Assert.False(Picker.UseInEveryCollectionCommand.CanExecute(null));

        _vm.AddTabCommand.Execute(null);
        Assert.False(Picker.UseInEveryCollectionCommand.CanExecute(null));
    }

    [Fact]
    public void ActivatesMatchingEnvironmentsIgnoringCase()
    {
        Select(_ordersQa);
        var saves = _service.SaveCallCount;

        Picker.UseInEveryCollectionCommand.Execute(null);

        Assert.Equal(_ordersQa.Id, _orders.ActiveEnvironmentId);
        Assert.Equal(_billingQa.Id, _billing.ActiveEnvironmentId);
        Assert.True(_service.SaveCallCount > saves);
    }

    [Fact]
    public void LeavesCollectionsWithoutAMatchUnchanged()
    {
        Select(_ordersQa);

        Picker.UseInEveryCollectionCommand.Execute(null);

        Assert.Equal(_shippingProd.Id, _shipping.ActiveEnvironmentId);
        Assert.Null(_plain.ActiveEnvironmentId);
    }

    [Fact]
    public void ReportsTheSkippedCollections()
    {
        Select(_ordersQa);

        Picker.UseInEveryCollectionCommand.Execute(null);

        Assert.Equal("'qa' is now active in 2 collections. These collections have no environment named 'qa' and were left unchanged: 'Shipping', 'Plain'.",
            Picker.UseInEveryCollectionReport);

        Picker.DismissUseInEveryCollectionReportCommand.Execute(null);
        Assert.Null(Picker.UseInEveryCollectionReport);
    }

    [Fact]
    public void ReportsWhenEveryCollectionMatched()
    {
        _shipping.Environments.Add(new RequestEnvironment { Name = "Dev" });
        _plain.Environments.Add(new RequestEnvironment { Name = "DEV" });
        Select(_ordersDev);

        Picker.UseInEveryCollectionCommand.Execute(null);

        Assert.Equal("'dev' is now active in all 4 collections.", Picker.UseInEveryCollectionReport);
        Assert.Equal(_shipping.Environments[1].Id, _shipping.ActiveEnvironmentId);
        Assert.Equal(_plain.Environments[0].Id, _plain.ActiveEnvironmentId);
    }

    [Fact]
    public void UpdatesTheSidebarLabels()
    {
        Select(_ordersQa);

        Picker.UseInEveryCollectionCommand.Execute(null);

        var labels = _vm.CollectionsPanel.Tree.Select(node => node.ActiveEnvironmentLabel).ToArray();
        Assert.Equal(new[] { "qa", " QA ", "prod", null }, labels);
    }
}
