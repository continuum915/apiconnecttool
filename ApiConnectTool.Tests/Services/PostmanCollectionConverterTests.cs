using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class PostmanCollectionConverterTests
{
    private readonly PostmanCollectionConverter _converter = new();

    private static RequestCollection BuildFixtureCollection()
    {
        var folder = new CollectionFolder { Name = "Admin", SortOrder = 0 };
        var listPets = new RequestItem
        {
            Name = "List pets",
            Url = "{{baseUrl}}/pets",
            Headers = { new KeyValueItem { Key = "Accept", Value = "application/json" } },
            Auth = new RequestAuth { Type = AuthType.Bearer, Token = "top-secret-token" }
        };
        var deletePet = new RequestItem
        {
            Name = "Delete pet",
            Method = HttpMethod.Delete,
            Url = "{{baseUrl}}/pets/{{id}}",
            FolderId = folder.Id
        };

        return new RequestCollection
        {
            Name = "Pets API",
            Folders = new ObservableCollection<CollectionFolder> { folder },
            Requests = new ObservableCollection<RequestItem> { listPets, deletePet }
        };
    }

    [Fact]
    public void RoundTrip_PreservesFolderStructureRequestFieldsAndVariableTokens()
    {
        var collection = BuildFixtureCollection();
        var json = _converter.Export(collection, includeSecrets: true);

        var imported = _converter.Import(json);

        Assert.Equal("Pets API", imported.Name);
        var importedFolder = Assert.Single(imported.Folders);
        Assert.Equal("Admin", importedFolder.Name);
        Assert.Equal(2, imported.Requests.Count);

        var listPets = imported.Requests.Single(r => r.Name == "List pets");
        Assert.Equal("{{baseUrl}}/pets", listPets.Url);
        Assert.Equal("Accept", listPets.Headers.Single().Key);
        Assert.Equal(AuthType.Bearer, listPets.Auth.Type);
        Assert.Equal("top-secret-token", listPets.Auth.Token.Value);
        Assert.Null(listPets.FolderId);

        var deletePet = imported.Requests.Single(r => r.Name == "Delete pet");
        Assert.Equal("{{baseUrl}}/pets/{{id}}", deletePet.Url);
        Assert.Equal(HttpMethod.Delete, deletePet.Method);
        Assert.Equal(importedFolder.Id, deletePet.FolderId);
    }

    [Fact]
    public void Export_DefaultExcludesSecrets_ButKeepsNonSecretAuthFields()
    {
        var collection = BuildFixtureCollection();

        var json = _converter.Export(collection, includeSecrets: false);

        Assert.DoesNotContain("top-secret-token", json);
        Assert.Contains("\"type\": \"bearer\"", json);
    }

    [Fact]
    public void Export_BasicAuth_RedactsPasswordButKeepsUsername()
    {
        var collection = new RequestCollection
        {
            Name = "Auth",
            Requests = { new RequestItem { Name = "R", Url = "https://api.test", Auth = new RequestAuth { Type = AuthType.Basic, Username = "alice", Password = "hunter2" } } }
        };

        var json = _converter.Export(collection, includeSecrets: false);

        Assert.Contains("alice", json);
        Assert.DoesNotContain("hunter2", json);
    }

    [Fact]
    public void Export_ApiKeyAuth_RedactsValueButKeepsKeyNameAndLocation()
    {
        var collection = new RequestCollection
        {
            Name = "Auth",
            Requests = { new RequestItem { Name = "R", Url = "https://api.test", Auth = new RequestAuth { Type = AuthType.ApiKey, ApiKeyHeaderName = "X-Api-Key", Token = "k-999" } } }
        };

        var json = _converter.Export(collection, includeSecrets: false);

        Assert.Contains("X-Api-Key", json);
        Assert.DoesNotContain("k-999", json);
    }

    [Fact]
    public void Import_FlatFixtureWithNoFolders_ImportsRequestsAtRoot()
    {
        const string json = """
            {
              "info": { "name": "Flat Collection", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
              "item": [
                {
                  "name": "Get user",
                  "request": {
                    "method": "GET",
                    "header": [ { "key": "Accept", "value": "application/json", "disabled": false } ],
                    "url": { "raw": "https://api.example.test/users/1", "query": [] }
                  }
                },
                {
                  "name": "Create user",
                  "request": {
                    "method": "POST",
                    "header": [],
                    "url": { "raw": "https://api.example.test/users", "query": [] },
                    "body": { "mode": "raw", "raw": "{\"name\":\"Mochi\"}", "options": { "raw": { "language": "json" } } }
                  }
                }
              ]
            }
            """;

        var imported = _converter.Import(json);

        Assert.Equal("Flat Collection", imported.Name);
        Assert.Empty(imported.Folders);
        Assert.Equal(2, imported.Requests.Count);
        Assert.All(imported.Requests, r => Assert.Null(r.FolderId));
        var createUser = imported.Requests.Single(r => r.Name == "Create user");
        Assert.Equal(RequestBodyMode.Raw, createUser.BodyMode);
        Assert.Equal("{\"name\":\"Mochi\"}", createUser.Body);
        Assert.Equal("application/json", createUser.BodyContentType);
    }

    [Fact]
    public void Import_UrlEncodedBody_ParsesFormDataRows()
    {
        const string json = """
            {
              "info": { "name": "Auth" },
              "item": [
                {
                  "name": "Token",
                  "request": {
                    "method": "POST",
                    "url": { "raw": "https://auth.example.test/token" },
                    "body": {
                      "mode": "urlencoded",
                      "urlencoded": [ { "key": "grant_type", "value": "client_credentials", "disabled": false } ]
                    }
                  }
                }
              ]
            }
            """;

        var imported = _converter.Import(json);

        var request = imported.Requests.Single();
        Assert.Equal(RequestBodyMode.UrlEncoded, request.BodyMode);
        Assert.Equal("client_credentials", request.FormData.Single().Value);
    }

    [Fact]
    public void Import_BasicAuth_MapsUsernameAndPassword()
    {
        const string json = """
            {
              "info": { "name": "Auth" },
              "item": [
                {
                  "name": "R",
                  "request": {
                    "method": "GET",
                    "url": { "raw": "https://api.example.test" },
                    "auth": {
                      "type": "basic",
                      "basic": [ { "key": "username", "value": "alice" }, { "key": "password", "value": "hunter2" } ]
                    }
                  }
                }
              ]
            }
            """;

        var imported = _converter.Import(json);

        var request = imported.Requests.Single();
        Assert.Equal(AuthType.Basic, request.Auth.Type);
        Assert.Equal("alice", request.Auth.Username);
        Assert.Equal("hunter2", request.Auth.Password.Value);
    }

    [Fact]
    public void Import_ApiKeyAuthInQuery_MapsToApiKeyQueryParam()
    {
        const string json = """
            {
              "info": { "name": "Auth" },
              "item": [
                {
                  "name": "R",
                  "request": {
                    "method": "GET",
                    "url": { "raw": "https://api.example.test" },
                    "auth": {
                      "type": "apikey",
                      "apikey": [
                        { "key": "key", "value": "api_key" },
                        { "key": "value", "value": "k-999" },
                        { "key": "in", "value": "query" }
                      ]
                    }
                  }
                }
              ]
            }
            """;

        var imported = _converter.Import(json);

        var request = imported.Requests.Single();
        Assert.Equal(AuthType.ApiKey, request.Auth.Type);
        Assert.Equal("api_key", request.Auth.ApiKeyQueryParam);
        Assert.Equal("k-999", request.Auth.Token.Value);
        Assert.Empty(request.Auth.ApiKeyHeaderName);
    }

    [Fact]
    public void Import_UnsupportedAuthScheme_LeavesRequestUnauthenticatedRatherThanFailing()
    {
        const string json = """
            {
              "info": { "name": "Auth" },
              "item": [
                {
                  "name": "R",
                  "request": {
                    "method": "GET",
                    "url": { "raw": "https://api.example.test" },
                    "auth": { "type": "oauth2" }
                  }
                }
              ]
            }
            """;

        var imported = _converter.Import(json);

        Assert.Equal(AuthType.None, imported.Requests.Single().Auth.Type);
    }

    [Fact]
    public void Import_MalformedJson_ThrowsPostmanImportException()
    {
        Assert.Throws<PostmanImportException>(() => _converter.Import("not json at all"));
    }

    [Fact]
    public void Import_JsonWithoutItemArray_ThrowsPostmanImportException()
    {
        Assert.Throws<PostmanImportException>(() => _converter.Import("""{ "info": { "name": "x" } }"""));
    }

    [Fact]
    public void Import_AssignsFreshIdsRatherThanReusingAnything()
    {
        var collection = BuildFixtureCollection();
        var json = _converter.Export(collection, includeSecrets: true);

        var first = _converter.Import(json);
        var second = _converter.Import(json);

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.Folders[0].Id, second.Folders[0].Id);
    }
}
