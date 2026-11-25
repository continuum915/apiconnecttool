using System.Net;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class CollectionRunnerViewModelTests
{
    [Fact]
    public async Task Start_ShowsRowsAndTotalsWithSelectedEnvironment()
    {
        var environment = new RequestEnvironment { Name = "dev", Variables = new() { new() { Key = "host", InitialValue = new("https://example.test") } } };
        var service = new CollectionRunner(RequestRunnerTests.Runner(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        var vm = new CollectionRunnerViewModel(new() { Requests = new() { new() { Name = "request", Url = "{{host}}" } } }, service, [environment], environment);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.Same(environment, vm.SelectedEnvironment);
        Assert.False(vm.IsRunning);
        Assert.Single(vm.Rows);
        Assert.Equal(1, vm.Report!.PassedCount);
        Assert.Equal("request", vm.CurrentRequest);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelAndClose_StopRunAndKeepProgress(bool close)
    {
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var service = new CollectionRunner(RequestRunnerTests.Runner(_ => {
            if (++calls == 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            waiting.SetResult();
            return new TaskCompletionSource<HttpResponseMessage>().Task;
        }));
        var vm = new CollectionRunnerViewModel(CollectionRunnerTests.Collection(3), service, [], null);
        var running = vm.StartCommand.ExecuteAsync(null);
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(vm.IsRunning);
        Assert.Single(vm.Rows);
        Assert.Equal(1, vm.Report!.RunCount);
        if (close) vm.Close(); else vm.CancelCommand.Execute(null);
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(vm.IsRunning);
        Assert.Equal(CollectionRunEndState.Cancelled, vm.Report!.EndState);
        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task InvalidDataAndDelay_PreventSending()
    {
        var calls = 0;
        var service = new CollectionRunner(RequestRunnerTests.Runner(_ => {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }));
        var vm = new CollectionRunnerViewModel(CollectionRunnerTests.Collection(1), service, [], null);
        vm.SetIterationData("rows.json", "[{\"x\":null}]");
        await vm.StartCommand.ExecuteAsync(null);
        Assert.Contains("element 1", vm.ErrorMessage);
        vm.ClearIterationDataCommand.Execute(null);
        vm.DelayMilliseconds = "-1";
        await vm.StartCommand.ExecuteAsync(null);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Equal(0, calls);
        Assert.Null(vm.Report);
    }

    [Fact]
    public async Task DataAndSettings_AreUsedForRun()
    {
        var calls = 0;
        var service = new CollectionRunner(RequestRunnerTests.Runner(_ => {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }));
        var collection = CollectionRunnerTests.Collection(2);
        collection.Requests[0].Assertions = new() { new() { ExpectedValue = "201" } };
        var vm = new CollectionRunnerViewModel(collection, service, [], null) { StopOnFailure = true, DelayMilliseconds = "1" };
        vm.SetIterationData("rows.csv", "value\none\ntwo\n");
        await vm.StartCommand.ExecuteAsync(null);
        Assert.Equal(1, calls);
        Assert.Equal(CollectionRunEndState.StoppedOnFailure, vm.Report!.EndState);
        Assert.Single(vm.Rows);
    }

    [Fact]
    public async Task FailedReplacementFile_ClearsPreviousData()
    {
        var calls = 0;
        var service = new CollectionRunner(RequestRunnerTests.Runner(_ => {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }));
        var vm = new CollectionRunnerViewModel(CollectionRunnerTests.Collection(1), service, [], null);
        vm.SetIterationData("rows.csv", "value\none\ntwo\n");
        vm.ShowDataError("read failed");
        Assert.Null(vm.IterationDataFileName);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.Equal(1, calls);
    }
}
