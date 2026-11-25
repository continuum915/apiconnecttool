using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class CollectionExportServiceTests
{
    private readonly CollectionExportService _service = new();

    private static RequestCollection BuildFixtureCollection()
    {
        var folder = new CollectionFolder { Name = "Admin", SortOrder = 0 };
        var rootRequest = new RequestItem
        {
            Name = "List pets",
            Url = "{{baseUrl}}/pets",
            Headers = { new KeyValueItem { Key = "Accept", Value = "application/json" } },
            Auth = new RequestAuth { Type = AuthType.Bearer, Token = "top-secret-token" }
        };
        var folderedRequest = new RequestItem
        {
            Name = "Delete pet",
            Method = System.Net.Http.HttpMethod.Delete,
            Url = "{{baseUrl}}/pets/{{id}}",
            FolderId = folder.Id
        };

        return new RequestCollection
        {
            Name = "Pets API",
            Folders = new ObservableCollection<CollectionFolder> { folder },
            Requests = new ObservableCollection<RequestItem> { rootRequest, folderedRequest }
        };
    }

    [Fact]
    public void Export_DefaultExcludesSecrets_ReturnsNoSecretValueAnywhereInJson()
    {
        var collection = BuildFixtureCollection();

        var json = _service.Export(collection, Guid.NewGuid(), includeSecrets: false);

        Assert.DoesNotContain("top-secret-token", json);
        Assert.False(System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("IncludesSecrets").GetBoolean());
    }

    [Fact]
    public void Export_IncludeSecretsTrue_WritesThePlaintextSecretValue()
    {
        var collection = BuildFixtureCollection();

        var json = _service.Export(collection, Guid.NewGuid(), includeSecrets: true);

        Assert.Contains("top-secret-token", json);
        Assert.True(System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("IncludesSecrets").GetBoolean());
    }

    [Fact]
    public void Export_DeclaresCurrentExportSchemaVersion()
    {
        var json = _service.Export(BuildFixtureCollection(), Guid.NewGuid(), includeSecrets: false);

        var version = System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("ExportSchemaVersion").GetInt32();
        Assert.Equal(CollectionExportService.CurrentExportSchemaVersion, version);
    }

    [Fact]
    public void RoundTrip_PreservesFolderStructureRequestFieldsAndVariableTokens()
    {
        var collection = BuildFixtureCollection();
        var json = _service.Export(collection, Guid.NewGuid(), includeSecrets: false);

        var imported = _service.Import(json);

        Assert.Equal("Pets API", imported.Name);
        var importedFolder = Assert.Single(imported.Folders);
        Assert.Equal("Admin", importedFolder.Name);
        Assert.Equal(2, imported.Requests.Count);

        var listPets = imported.Requests.Single(r => r.Name == "List pets");
        Assert.Equal("{{baseUrl}}/pets", listPets.Url);
        Assert.Equal("Accept", listPets.Headers.Single().Key);
        Assert.Null(listPets.FolderId);

        var deletePet = imported.Requests.Single(r => r.Name == "Delete pet");
        Assert.Equal("{{baseUrl}}/pets/{{id}}", deletePet.Url);
        Assert.Equal(System.Net.Http.HttpMethod.Delete, deletePet.Method);
        Assert.Equal(importedFolder.Id, deletePet.FolderId);
    }

    [Fact]
    public void Import_AssignsFreshIdsThroughoutRatherThanReusingExportedOnes()
    {
        var collection = BuildFixtureCollection();
        var originalFolderId = collection.Folders[0].Id;
        var originalRequestIds = collection.Requests.Select(r => r.Id).ToHashSet();
        var originalCollectionId = collection.Id;
        var json = _service.Export(collection, Guid.NewGuid(), includeSecrets: false);

        var imported = _service.Import(json);

        Assert.NotEqual(originalCollectionId, imported.Id);
        Assert.NotEqual(originalFolderId, imported.Folders[0].Id);
        Assert.DoesNotContain(imported.Requests, r => originalRequestIds.Contains(r.Id));
        // Folder references still resolve correctly against the new IDs.
        Assert.All(imported.Requests.Where(r => r.FolderId is not null), r => Assert.Equal(imported.Folders[0].Id, r.FolderId));
    }

    [Fact]
    public void Import_NewerExportSchemaVersion_ThrowsWithClearError()
    {
        var json = """{ "ExportSchemaVersion": 999, "Collection": { "Name": "x" } }""";

        var ex = Assert.Throws<InvalidDataException>(() => _service.Import(json));
        Assert.Contains("newer version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Import_MalformedJson_ThrowsInvalidDataException()
    {
        Assert.Throws<InvalidDataException>(() => _service.Import("not json at all"));
    }

    [Fact]
    public void Import_MissingExportSchemaVersion_ThrowsInvalidDataException()
    {
        Assert.Throws<InvalidDataException>(() => _service.Import("""{ "Collection": {} }"""));
    }
}
