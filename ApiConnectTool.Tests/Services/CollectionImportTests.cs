using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class CollectionImportTests
{
    internal const string Postman = """
        {
          "info": { "name": "Pets", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
          "item": [
            { "name": "Admin", "item": [ { "name": "Purge", "request": { "method": "DELETE", "url": "https://pets.test/purge" } } ] },
            { "name": "List pets", "request": { "method": "GET", "url": "https://pets.test/pets" } }
          ]
        }
        """;

    internal const string OpenApi = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Shop", "version": "1" },
          "servers": [ { "url": "https://shop.test" } ],
          "paths": {
            "/orders": { "get": { "tags": ["orders"], "summary": "List orders", "responses": { "200": { "description": "ok" } } } },
            "/health": { "get": { "summary": "Health", "responses": { "200": { "description": "ok" } } } }
          }
        }
        """;

    internal const string Curl = "curl -X POST https://api.test/items -H 'Accept: application/json' -d '{\"a\":1}'";

    internal static string ApiConnectToolExport()
    {
        var folder = new CollectionFolder { Name = "Admin" };
        var collection = new RequestCollection
        {
            Name = "Orders", Folders = { folder },
            Requests = { new() { Name = "Purge", FolderId = folder.Id, Url = "https://orders.test/purge" }, new() { Name = "List", Url = "https://orders.test" } },
            Environments = { new RequestEnvironment { Name = "dev" } }
        };
        return new CollectionExportService().Export(collection, Guid.NewGuid(), includeSecrets: false);
    }

    public static TheoryData<ImportFormat, string> Sources() => new()
    {
        { ImportFormat.ApiConnectTool, ApiConnectToolExport() },
        { ImportFormat.Postman, Postman },
        { ImportFormat.OpenApi, OpenApi },
        { ImportFormat.Curl, Curl }
    };

    [Theory]
    [MemberData(nameof(Sources))]
    public void CommitCreatesExactlyWhatThePreviewShows(ImportFormat format, string text)
    {
        var preview = new CollectionImport().Read(format, text);
        var everything = preview.Collection.Folders.Select(folder => folder.Id)
            .Concat(preview.Collection.Requests.Select(request => request.Id))
            .Concat(preview.Collection.Environments.Select(environment => environment.Id)).ToHashSet();

        var created = CollectionImport.Commit(preview, "Imported", importEnvironments: true, everything);

        Assert.Equal("Imported", created.Name);
        Assert.Equal(preview.FolderCount, created.Folders.Count);
        Assert.Equal(preview.RequestCount, created.Requests.Count);
        Assert.Equal(preview.Collection.Requests.Select(request => request.Name), created.Requests.Select(request => request.Name));
        Assert.True(preview.RequestCount > 0);
        Assert.Null(created.ActiveEnvironmentId);
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public void UntickedItemsAreNotCreated(ImportFormat format, string text)
    {
        var preview = new CollectionImport().Read(format, text);
        var first = preview.Collection.Requests[0];
        var included = preview.Collection.Requests.Skip(1).Select(request => request.Id)
            .Concat(preview.Collection.Folders.Select(folder => folder.Id)).ToHashSet();

        var created = CollectionImport.Commit(preview, "Imported", importEnvironments: false, included);

        Assert.DoesNotContain(created.Requests, request => request.Id == first.Id);
        Assert.Equal(preview.RequestCount - 1, created.Requests.Count);
        Assert.Empty(created.Environments);
    }

    [Fact]
    public void AnUntickedFolderSkipsEverythingInside()
    {
        var preview = new CollectionImport().Read(ImportFormat.Postman, Postman);
        var included = preview.Collection.Requests.Select(request => request.Id).ToHashSet();
        var created = CollectionImport.Commit(preview, "Pets", false, included);
        Assert.Empty(created.Folders);
        Assert.Equal("List pets", Assert.Single(created.Requests).Name);
    }

    [Theory]
    [InlineData(ImportFormat.ApiConnectTool, "{ not json")]
    [InlineData(ImportFormat.Postman, "{\"info\": 3}")]
    [InlineData(ImportFormat.OpenApi, "{\"swagger\": \"2.0\"}")]
    [InlineData(ImportFormat.Curl, "wget https://example.test")]
    public void UnreadableSourcesThrowTheImportersError(ImportFormat format, string text) =>
        Assert.ThrowsAny<Exception>(() => new CollectionImport().Read(format, text));

    [Fact]
    public void ApiConnectToolPreviewCountsEnvironments()
    {
        var preview = new CollectionImport().Read(ImportFormat.ApiConnectTool, ApiConnectToolExport());
        Assert.Equal(1, preview.EnvironmentCount);
        Assert.Contains("1 folders, 2 requests, 1 collection environments", preview.DetectionSummary);
    }
}
