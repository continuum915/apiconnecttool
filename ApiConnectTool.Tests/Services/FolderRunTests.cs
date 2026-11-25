using System.Net;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.ViewModels;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class FolderRunTests
{
    [Fact]
    public async Task FolderRunnerIncludesDescendantsInSidebarOrderAndKeepsParentAuthentication()
    {
        var selected = new CollectionFolder { Name = "Selected", Auth = new() { Type = AuthType.Bearer, Token = "parent-token" } };
        var child = new CollectionFolder { Name = "Child", ParentFolderId = selected.Id };
        var sibling = new CollectionFolder { Name = "Sibling" };
        RequestItem Request(string name, Guid? folder, int order = 0) => new() { Name = name, Url = "https://example.test/" + name, FolderId = folder, SortOrder = order, Auth = new() { Type = AuthType.Inherit } };
        var collection = new RequestCollection { Folders = new() { sibling, child, selected }, Requests = new() {
            Request("root", null), Request("nested", child.Id), Request("later", selected.Id, 9), Request("first", selected.Id, 1), Request("excluded", sibling.Id) } };
        var urls = new List<string>();
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        {
            urls.Add(message.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer parent-token", message.Headers.Authorization!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        })));
        var main = new MainWindowViewModel(new FakeWorkspaceService { WorkspaceToReturn = new() { Collections = new() { collection } } }, service);
        var runner = main.CreateFolderRunner(selected);
        await runner.StartCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "/first", "/later", "/nested" }, urls);
        Assert.Equal(3, runner.Report!.PassedCount);
        Assert.Equal(5, collection.Requests.Count);
    }

    [Fact]
    public async Task UnknownFolderDoesNotAccidentallyRunTheWholeCollection()
    {
        var sends = 0;
        var runner = new CollectionRunner(RequestRunnerTests.Runner(_ => { sends++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); }));
        var report = await runner.RunAsync(CollectionRunnerTests.Collection(2), configuration: new() { FolderId = Guid.NewGuid() });
        Assert.Equal(0, sends);
        Assert.Equal(0, report.RunCount);
    }
}
