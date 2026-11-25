using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class CollectionsViewModelImportExportTests
{
    [Theory]
    [InlineData("native")]
    [InlineData("postman")]
    [InlineData("openapi")]
    public void Import_RefreshesTreeAndSearchBeforePersisting_EvenWhenSaveFails(string format)
    {
        var original = new RequestCollection { Name = "Pets" };
        original.Requests.Add(new RequestItem { Name = "List pets", Url = "https://example.test/pets" });
        var saves = 0;
        CollectionsViewModel? vm = null;
        vm = new CollectionsViewModel(new ObservableCollection<RequestCollection> { original }, persist: () =>
        {
            saves++;
            Assert.Equal(2, vm!.Tree.Count);
            Assert.Equal(2, vm.SearchResults.Count);
            return false;
        });
        vm.SearchText = "pets";

        switch (format)
        {
            case "native": vm.ImportCollection(vm.ExportCollection(original, false)); break;
            case "postman": vm.ImportPostmanCollection(vm.ExportCollectionAsPostman(original, false)); break;
            default:
                vm.ImportOpenApiCollection("""
                    {"openapi":"3.0.0","info":{"title":"Pets"},"paths":{"/pets":{"get":{"summary":"List pets"}}}}
                    """);
                break;
        }

        Assert.Equal(1, saves);
        Assert.Equal("Pets 1", vm.Collections[1].Name);
        Assert.NotEqual(original.Id, vm.Collections[1].Id);
    }

    [Fact]
    public void NativeExport_UsesCurrentWorkspaceId()
    {
        var workspaceId = Guid.NewGuid();
        var vm = new CollectionsViewModel(new(), workspaceId: () => workspaceId);
        workspaceId = Guid.NewGuid();

        using var document = JsonDocument.Parse(vm.ExportCollection(new RequestCollection(), false));

        Assert.Equal(workspaceId, document.RootElement.GetProperty("SourceWorkspaceId").GetGuid());
        Assert.False(document.RootElement.GetProperty("IncludesSecrets").GetBoolean());
    }

    [Fact]
    public void CurlImport_OpensFreshRequestsWithoutPersistingOrAddingCollections()
    {
        var opened = new System.Collections.Generic.List<RequestItem>();
        var vm = new CollectionsViewModel(new(), openRequest: opened.Add,
            persist: () => throw new InvalidOperationException("cURL must not save"));

        vm.ImportFromCurl("curl https://example.test/pets");
        vm.ImportFromCurl("curl https://example.test/pets");

        Assert.Equal(2, opened.Count);
        Assert.NotEqual(opened[0].Id, opened[1].Id);
        Assert.Empty(vm.Collections);
    }

    [Theory]
    [InlineData("native")]
    [InlineData("postman")]
    [InlineData("openapi")]
    public void Import_SaveFailureSurfacesExistingWorkspaceSaveError(string format)
    {
        var service = new FakeWorkspaceService();
        var main = new MainWindowViewModel(service);
        service.SaveException = new IOException("disk full");
        var collection = new RequestCollection { Name = "Pets" };
        switch (format)
        {
            case "native": main.CollectionsPanel.ImportCollection(main.CollectionsPanel.ExportCollection(collection, false)); break;
            case "postman": main.CollectionsPanel.ImportPostmanCollection(main.CollectionsPanel.ExportCollectionAsPostman(collection, false)); break;
            default: main.CollectionsPanel.ImportOpenApiCollection("""{"openapi":"3.0.0","info":{"title":"Pets"},"paths":{}}"""); break;
        }

        Assert.Equal("disk full", main.WorkspaceSaveError);
        Assert.Single(main.CollectionsPanel.Tree);
    }

    [Fact]
    public void OpenApiImport_ReturnsSecurityWarnings()
    {
        var vm = new CollectionsViewModel(new());
        var report = vm.ImportOpenApiCollection("""
            {"openapi":"3.0.0","info":{"title":"Pets"},"security":[{"unknown":[]}],"paths":{"/pets":{"get":{}}}}
            """);

        Assert.True(report.HasWarnings);
        Assert.Equal(1, report.SecuredRequestCount);
        Assert.Single(Assert.Single(vm.Collections).Requests);
    }
}
