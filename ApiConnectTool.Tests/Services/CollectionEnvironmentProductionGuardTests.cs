using System.Net;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.ViewModels;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.Services;

/// <summary>The production send warning covers the active collection environment and the active
/// global environment, prompts once per send, and names each production environment.</summary>
public class CollectionEnvironmentProductionGuardTests
{
    private static RequestEnvironment Env(string name, bool production) => new() { Name = name, IsProduction = production,
        Variables = { new() { Key = "host", InitialValue = $"https://{name}.example.test" } } };

    private sealed class Setup
    {
        public required Workspace Workspace { get; init; }
        public required RequestCollection Orders { get; init; }
        public required RequestEnvironment OrdersDev { get; init; }
        public required RequestEnvironment OrdersProd { get; init; }
        public required RequestEnvironment GlobalDev { get; init; }
        public required RequestEnvironment GlobalProd { get; init; }
        public List<IReadOnlyList<ProductionTarget>> Prompts { get; } = new();
        public List<string> Sent { get; } = new();
        public bool Proceed { get; set; } = true;
        public MainWindowViewModel? Main { get; set; }

        public async Task SendAsync(RequestEnvironment? collectionEnvironment, RequestEnvironment global)
        {
            Orders.ActiveEnvironmentId = collectionEnvironment?.Id;
            Main!.SelectedEnvironment = global;
            await Main.ActiveTab!.SendCommand.ExecuteAsync(null);
        }
    }

    private static Setup Create(ProductionWarningMode mode)
    {
        var ordersDev = Env("orders-dev", false);
        var ordersProd = Env("orders-prod", true);
        var globalDev = Env("global-dev", false);
        var globalProd = Env("global-prod", true);
        var orders = new RequestCollection { Name = "Orders", Environments = { ordersDev, ordersProd } };
        orders.Requests.Add(new RequestItem { Name = "ping", Url = "{{host}}/ping" });
        var setup = new Setup
        {
            Workspace = new Workspace { Environments = { globalDev, globalProd }, Collections = { orders }, ProductionWarningMode = mode },
            Orders = orders, OrdersDev = ordersDev, OrdersProd = ordersProd, GlobalDev = globalDev, GlobalProd = globalProd
        };
        var execution = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        {
            setup.Sent.Add(message.RequestUri!.Host);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        })));
        setup.Main = new MainWindowViewModel(new FakeWorkspaceService { WorkspaceToReturn = setup.Workspace }, execution)
        {
            ProductionSendConfirmationHandler = targets => { setup.Prompts.Add(targets); return Task.FromResult(setup.Proceed); }
        };
        setup.Main.OpenRequestCommand.Execute(orders.Requests[0]);
        return setup;
    }

    [Fact]
    public async Task ProductionCollectionEnvironmentWarnsAndNamesItsCollection()
    {
        var setup = Create(ProductionWarningMode.AlwaysWarn);

        await setup.SendAsync(setup.OrdersProd, setup.GlobalDev);

        var target = Assert.Single(Assert.Single(setup.Prompts));
        Assert.Same(setup.OrdersProd, target.Environment);
        Assert.Equal("Orders", target.CollectionName);
        Assert.Equal(new[] { "orders-prod.example.test" }, setup.Sent);
    }

    [Fact]
    public async Task ProductionGlobalEnvironmentStillWarnsWithACollectionEnvironmentActive()
    {
        var setup = Create(ProductionWarningMode.AlwaysWarn);

        await setup.SendAsync(setup.OrdersDev, setup.GlobalProd);

        var target = Assert.Single(Assert.Single(setup.Prompts));
        Assert.Same(setup.GlobalProd, target.Environment);
        Assert.Null(target.CollectionName);
    }

    [Fact]
    public async Task BothEnvironmentsInProductionPromptOnceNamingBoth()
    {
        var setup = Create(ProductionWarningMode.AlwaysWarn);

        await setup.SendAsync(setup.OrdersProd, setup.GlobalProd);

        var prompt = Assert.Single(setup.Prompts);
        Assert.Equal(new[] { setup.OrdersProd, setup.GlobalProd }, prompt.Select(target => target.Environment));
        Assert.Single(setup.Sent);
    }

    [Fact]
    public async Task NoProductionEnvironmentSendsWithoutPrompting()
    {
        var setup = Create(ProductionWarningMode.AlwaysWarn);

        await setup.SendAsync(setup.OrdersDev, setup.GlobalDev);
        await setup.SendAsync(null, setup.GlobalDev);

        Assert.Empty(setup.Prompts);
        Assert.Equal(2, setup.Sent.Count);
    }

    [Fact]
    public async Task DecliningTheWarningCancelsTheSend()
    {
        var setup = Create(ProductionWarningMode.AlwaysWarn);
        setup.Proceed = false;

        await setup.SendAsync(setup.OrdersProd, setup.GlobalDev);

        Assert.Single(setup.Prompts);
        Assert.Empty(setup.Sent);
    }

    [Fact]
    public async Task OncePerSessionIsTrackedForEachEnvironment()
    {
        var setup = Create(ProductionWarningMode.WarnOncePerSession);

        await setup.SendAsync(setup.OrdersProd, setup.GlobalDev);
        await setup.SendAsync(setup.OrdersProd, setup.GlobalDev);
        await setup.SendAsync(setup.OrdersDev, setup.GlobalProd);
        await setup.SendAsync(setup.OrdersProd, setup.GlobalProd);
        await setup.SendAsync(null, setup.GlobalProd);

        Assert.Equal(new[] { setup.OrdersProd, setup.GlobalProd }, setup.Prompts.Select(prompt => Assert.Single(prompt).Environment));
        Assert.Equal(5, setup.Sent.Count);
    }

    [Fact]
    public async Task OncePerSessionPromptsAgainWhenASendAddsAnUnconfirmedEnvironment()
    {
        var setup = Create(ProductionWarningMode.WarnOncePerSession);

        await setup.SendAsync(setup.OrdersProd, setup.GlobalDev);
        await setup.SendAsync(setup.OrdersProd, setup.GlobalProd);
        await setup.SendAsync(setup.OrdersProd, setup.GlobalProd);

        Assert.Equal(2, setup.Prompts.Count);
        Assert.Equal(new[] { setup.OrdersProd, setup.GlobalProd }, setup.Prompts[1].Select(target => target.Environment));
    }

    [Fact]
    public async Task DownloadWarnsWithTheEnvironmentsOfTheSendItRepeats()
    {
        var setup = Create(ProductionWarningMode.AlwaysWarn);
        await setup.SendAsync(setup.OrdersProd, setup.GlobalDev);
        setup.Orders.ActiveEnvironmentId = setup.OrdersDev.Id;
        var destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
        try
        {
            Assert.True(await setup.Main!.ActiveTab!.DownloadResponseAsync(destination));
        }
        finally { File.Delete(destination); }

        Assert.Equal(2, setup.Prompts.Count);
        var target = Assert.Single(setup.Prompts[1]);
        Assert.Equal(setup.OrdersProd.Id, target.Environment.Id);
        Assert.Equal("Orders", target.CollectionName);
    }

    [Theory]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(true, true, 1)]
    [InlineData(false, false, 0)]
    public async Task CollectionRunnerPromptsOnceForTheRunsEnvironments(bool collectionProduction, bool globalProduction, int prompts)
    {
        var setup = Create(ProductionWarningMode.AlwaysWarn);
        var runner = setup.Main!.CreateCollectionRunner(setup.Orders);
        runner.SelectedCollectionEnvironment = runner.CollectionEnvironments.Single(choice =>
            choice.Environment == (collectionProduction ? setup.OrdersProd : setup.OrdersDev));
        runner.SelectedEnvironment = globalProduction ? setup.GlobalProd : setup.GlobalDev;

        await runner.StartCommand.ExecuteAsync(null);

        var expectedCollections = new List<string?>();
        if (collectionProduction) expectedCollections.Add("Orders");
        if (globalProduction) expectedCollections.Add(null);
        Assert.Equal(prompts, setup.Prompts.Count);
        Assert.Equal(expectedCollections, setup.Prompts.SelectMany(prompt => prompt).Select(target => target.CollectionName));
        Assert.Single(setup.Sent);
        Assert.Null(setup.Orders.ActiveEnvironmentId);
    }

    [Fact]
    public async Task CollectionRunnerDefaultsToTheActiveProductionCollectionEnvironment()
    {
        var setup = Create(ProductionWarningMode.AlwaysWarn);
        setup.Orders.ActiveEnvironmentId = setup.OrdersProd.Id;
        setup.Proceed = false;

        await setup.Main!.CreateCollectionRunner(setup.Orders).StartCommand.ExecuteAsync(null);

        Assert.Same(setup.OrdersProd, Assert.Single(Assert.Single(setup.Prompts)).Environment);
        Assert.Empty(setup.Sent);
    }

    [Fact]
    public void MessageNamesEachEnvironmentAndTheCollectionOfACollectionEnvironment()
    {
        var single = ProductionSendMessage.Format([new(new RequestEnvironment { Name = "prod" }, "Orders")]);
        var both = ProductionSendMessage.Format([new(new RequestEnvironment { Name = "prod" }, "Orders"), new(new RequestEnvironment { Name = "shared prod" })]);

        Assert.Contains("'prod' (environment of collection 'Orders')", single);
        Assert.Contains("an environment marked as production", single);
        Assert.Contains("'prod' (environment of collection 'Orders')", both);
        Assert.Contains("'shared prod' (global environment)", both);
        Assert.Contains("environments marked as production", both);
    }
}
