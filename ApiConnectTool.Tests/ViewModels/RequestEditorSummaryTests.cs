using System.Collections.ObjectModel;
using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class RequestEditorSummaryTests
{
    [Fact]
    public void Counts_AreEnabledRowsWithAKey_AndUpdateLive()
    {
        var tab = new RequestTabViewModel();
        Assert.Equal("Params (0)", tab.Summary.ParamsHeader);
        Assert.Equal("Headers (0)", tab.Summary.HeadersHeader);
        Assert.Equal("Assertions (0)", tab.Summary.AssertionsHeader);

        tab.QueryParams.Add(new KeyValueItem { Key = "page", Value = "1" });
        tab.QueryParams.Add(new KeyValueItem { Key = "debug", IsEnabled = false });
        Assert.Equal(1, tab.Summary.ParamsCount);
        Assert.Equal("Params (1)", tab.Summary.ParamsHeader);

        tab.QueryParams.Single(row => row.Key == "debug").IsEnabled = true;
        Assert.Equal(2, tab.Summary.ParamsCount);

        tab.RequestHeaders.Add(new KeyValueItem { Key = "Accept", Value = "application/json" });
        tab.RequestHeaders.Add(new KeyValueItem { Key = "X-Off", IsEnabled = false });
        Assert.Equal("Headers (1)", tab.Summary.HeadersHeader);

        tab.AssertionTests.AddAssertionCommand.Execute(null);
        tab.AssertionTests.AddAssertionCommand.Execute(null);
        tab.AssertionTests.Assertions[1].IsEnabled = false;
        Assert.Equal("Assertions (1)", tab.Summary.AssertionsHeader);
    }

    [Fact]
    public void LoadedRequest_CountsItsRows()
    {
        var request = new RequestItem
        {
            Name = "List", Url = "https://example.test/items?a=1&b=2",
            Headers = { new KeyValueItem { Key = "Accept", Value = "*/*" } }
        };
        var tab = RequestTabViewModel.FromRequest(request);
        Assert.Equal(2, tab.Summary.ParamsCount);
        Assert.Equal(1, tab.Summary.HeadersCount);
    }

    private static (MainWindowViewModel Vm, RequestCollection Orders, RequestItem Request, RequestEnvironment Dev, RequestEnvironment Prod, RequestEnvironment GlobalProd) Arrange()
    {
        var dev = new RequestEnvironment { Name = "dev" };
        var prod = new RequestEnvironment { Name = "prod", IsProduction = true };
        var globalProd = new RequestEnvironment { Name = "live", IsProduction = true };
        var request = new RequestItem { Name = "List orders", Url = "https://orders.test" };
        var orders = new RequestCollection { Name = "Orders", Environments = { dev, prod }, ActiveEnvironmentId = dev.Id, Requests = { request } };
        var workspace = new Workspace
        {
            Collections = new ObservableCollection<RequestCollection> { orders },
            Environments = new ObservableCollection<RequestEnvironment> { globalProd }
        };
        return (new MainWindowViewModel(new FakeWorkspaceService { WorkspaceToReturn = workspace }), orders, request, dev, prod, globalProd);
    }

    [Fact]
    public void ProductionStrip_FollowsTheCollectionEnvironment()
    {
        var (vm, orders, request, _, prod, _) = Arrange();
        vm.OpenRequestCommand.Execute(request);
        var summary = vm.ActiveTab!.Summary;
        Assert.False(summary.IsProduction);

        vm.CollectionEnvironmentPicker.SelectedChoice = vm.CollectionEnvironmentPicker.Choices.Single(choice => choice.Environment == prod);
        Assert.Equal(prod.Id, orders.ActiveEnvironmentId);
        Assert.True(summary.IsProduction);
        Assert.Contains("'prod' (environment of collection 'Orders')", summary.ProductionWarning);
        Assert.Contains("every time", summary.ProductionWarning);

        vm.ProductionWarningMode = ProductionWarningMode.NeverWarn;
        Assert.Contains("turned off", summary.ProductionWarning);
    }

    [Fact]
    public void ProductionStrip_FollowsTheGlobalEnvironment_AlsoForDraftTabs()
    {
        var (vm, _, request, _, _, globalProd) = Arrange();
        vm.AddTabCommand.Execute(null);
        var draft = vm.ActiveTab!.Summary;
        vm.OpenRequestCommand.Execute(request);
        var saved = vm.ActiveTab!.Summary;
        Assert.False(draft.IsProduction);
        Assert.False(saved.IsProduction);

        vm.SelectedEnvironment = globalProd;
        Assert.True(draft.IsProduction);
        Assert.True(saved.IsProduction);
        Assert.Contains("'live' (global environment)", draft.ProductionWarning);

        vm.SelectedEnvironment = null;
        Assert.False(draft.IsProduction);
    }
}
