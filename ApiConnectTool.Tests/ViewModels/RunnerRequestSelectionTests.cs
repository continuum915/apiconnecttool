using System.Net;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class RunnerRequestSelectionTests
{
    private readonly CollectionFolder _folder = new() { Name = "Admin" };
    private readonly RequestCollection _collection;

    public RunnerRequestSelectionTests()
    {
        _collection = new RequestCollection
        {
            Name = "Api", Folders = { _folder },
            Requests =
            {
                new() { Name = "root", Url = "https://example.test/root" },
                new() { Name = "first", FolderId = _folder.Id, Url = "https://example.test/first", SortOrder = 0 },
                new() { Name = "second", FolderId = _folder.Id, Url = "https://example.test/second", SortOrder = 1 }
            }
        };
    }

    [Fact]
    public async Task Runner_RunsOnlyIncludedRequestsInRunOrderAcrossIterations()
    {
        var urls = new List<string>();
        var runner = new CollectionRunner(RequestRunnerTests.Runner(message =>
        {
            urls.Add(message.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }));
        var included = new HashSet<Guid> { _collection.Requests[0].Id, _collection.Requests[2].Id };
        var report = await ((ICollectionRunner)runner).RunAsync(_collection, new CollectionRunConfiguration
        {
            IncludedRequestIds = included,
            IterationRows = [new Dictionary<string, string>(), new Dictionary<string, string>()]
        });

        Assert.Equal(new[] { "/second", "/root", "/second", "/root" }, urls);
        Assert.Equal(new[] { "second", "root", "second", "root" }, report.Entries.Select(entry => entry.RequestName));
    }

    [Fact]
    public async Task Runner_WithNullIncludedIds_RunsEverything()
    {
        var urls = new List<string>();
        var runner = new CollectionRunner(RequestRunnerTests.Runner(message =>
        {
            urls.Add(message.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }));
        await ((ICollectionRunner)runner).RunAsync(_collection, new CollectionRunConfiguration { IncludedRequestIds = null });
        Assert.Equal(new[] { "/first", "/second", "/root" }, urls);
    }

    [Fact]
    public void Checklist_ListsRequestsInRunOrder_AllIncludedByDefault()
    {
        var vm = new CollectionRunnerViewModel(_collection, new RecordingRunner(), [], null);
        Assert.Equal(new[] { "first", "second", "root" }, vm.RunRequests.Select(choice => choice.Name));
        Assert.Equal(new[] { "Admin", "Admin", null }, vm.RunRequests.Select(choice => choice.FolderName));
        Assert.All(vm.RunRequests, choice => Assert.True(choice.IsIncluded));
        Assert.Equal(3, vm.IncludedCount);
        Assert.Equal("Start (3 requests)", vm.StartText);
        Assert.Null(vm.IncludedRequestIds);
        Assert.True(vm.StartCommand.CanExecute(null));
    }

    [Fact]
    public void SelectNoneAndAll_UpdateTheCountAndStart()
    {
        var vm = new CollectionRunnerViewModel(_collection, new RecordingRunner(), [], null);
        vm.SelectNoneCommand.Execute(null);
        Assert.Equal(0, vm.IncludedCount);
        Assert.Equal("Start (0 requests)", vm.StartText);
        Assert.False(vm.StartCommand.CanExecute(null));

        vm.RunRequests[1].IsIncluded = true;
        Assert.Equal("Start (1 requests)", vm.StartText);
        Assert.True(vm.StartCommand.CanExecute(null));
        Assert.Equal(new HashSet<Guid> { _collection.Requests[2].Id }, vm.IncludedRequestIds);

        vm.SelectAllCommand.Execute(null);
        Assert.Equal(3, vm.IncludedCount);
        Assert.Null(vm.IncludedRequestIds);
    }

    [Fact]
    public async Task Start_PassesTheIncludedIds()
    {
        var runner = new RecordingRunner();
        var vm = new CollectionRunnerViewModel(_collection, runner, [], null);
        vm.RunRequests[0].IsIncluded = false;
        await vm.StartCommand.ExecuteAsync(null);
        Assert.Equal(new HashSet<Guid> { _collection.Requests[2].Id, _collection.Requests[0].Id }, runner.Configuration!.IncludedRequestIds);

        vm.SelectAllCommand.Execute(null);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.Null(runner.Configuration!.IncludedRequestIds);
    }

    [Fact]
    public void FolderRun_ListsOnlyThatFoldersRequests()
    {
        var vm = new CollectionRunnerViewModel(_collection, new RecordingRunner(), [], null, _folder);
        Assert.Equal(new[] { "first", "second" }, vm.RunRequests.Select(choice => choice.Name));
    }

    private sealed class RecordingRunner : ICollectionRunner
    {
        public CollectionRunConfiguration? Configuration { get; private set; }

        public Task<CollectionRunReport> RunAsync(RequestCollection collection, CollectionRunConfiguration settings,
            IProgress<CollectionRunProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Configuration = settings;
            return Task.FromResult(new CollectionRunReport([], CollectionRunEndState.Completed, TimeSpan.Zero));
        }
    }
}
