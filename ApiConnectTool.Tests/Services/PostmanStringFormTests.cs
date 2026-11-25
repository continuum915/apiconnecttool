using System.Linq;
using System.Net.Http;
using System.Text.Json;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class PostmanStringFormTests
{
    private readonly PostmanCollectionConverter _converter = new();

    [Fact]
    public void StringUrl_BecomesRawUrlAndQuerySplitsIntoParams()
    {
        const string json = """
            {
              "info": { "name": "Strings" },
              "item": [
                { "name": "Search", "request": { "method": "POST", "url": "https://api.example.test/search?q=apiconnecttool&limit=5" } }
              ]
            }
            """;

        var request = Assert.Single(_converter.Import(json).Requests);

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.example.test/search?q=apiconnecttool&limit=5", request.Url);
        var tab = RequestTabViewModel.FromRequest(request);
        Assert.Equal("https://api.example.test/search?q=apiconnecttool&limit=5", tab.RequestUrl);
        Assert.Equal(["q=apiconnecttool", "limit=5"], tab.QueryParams.Where(row => !string.IsNullOrEmpty(row.Key)).Select(row => row.Key + "=" + row.Value));
    }

    [Fact]
    public void StringRequest_ImportsAsGet()
    {
        const string json = """
            {
              "info": { "name": "Strings" },
              "item": [ { "name": "Health", "request": "{{baseUrl}}/health" } ]
            }
            """;

        var request = Assert.Single(_converter.Import(json).Requests);

        Assert.Equal("Health", request.Name);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("{{baseUrl}}/health", request.Url);
        Assert.Empty(request.Headers);
        Assert.Equal(RequestBodyMode.None, request.BodyMode);
    }

    [Fact]
    public void MixedForms_PreserveFoldersAndOrder()
    {
        const string json = """
            {
              "info": { "name": "Mixed" },
              "item": [
                { "name": "First", "request": "https://api.example.test/first" },
                {
                  "name": "Users",
                  "item": [
                    { "name": "List", "request": { "method": "GET", "url": { "raw": "https://api.example.test/users" } } },
                    { "name": "Create", "request": { "method": "POST", "url": "https://api.example.test/users" } },
                    { "name": "Ping", "request": "https://api.example.test/ping" }
                  ]
                },
                { "name": "Last", "request": { "method": "DELETE", "url": "https://api.example.test/last" } }
              ]
            }
            """;

        var collection = _converter.Import(json);

        var folder = Assert.Single(collection.Folders);
        Assert.Equal("Users", folder.Name);
        Assert.Equal(["First", "Last"], collection.Requests.Where(r => r.FolderId is null).OrderBy(r => r.SortOrder).Select(r => r.Name));
        Assert.Equal(["List", "Create", "Ping"], collection.Requests.Where(r => r.FolderId == folder.Id).OrderBy(r => r.SortOrder).Select(r => r.Name));
        Assert.Equal(HttpMethod.Post, collection.Requests.Single(r => r.Name == "Create").Method);
        Assert.Equal(HttpMethod.Delete, collection.Requests.Single(r => r.Name == "Last").Method);
        Assert.Equal("https://api.example.test/ping", collection.Requests.Single(r => r.Name == "Ping").Url);
    }

    [Fact]
    public void Export_StillWritesObjectUrls()
    {
        var collection = _converter.Import("""
            { "info": { "name": "Strings" }, "item": [ { "name": "Health", "request": "https://api.example.test/health" } ] }
            """);

        using var exported = JsonDocument.Parse(_converter.Export(collection, includeSecrets: false));

        var request = exported.RootElement.GetProperty("item")[0].GetProperty("request");
        Assert.Equal(JsonValueKind.Object, request.ValueKind);
        var url = request.GetProperty("url");
        Assert.Equal(JsonValueKind.Object, url.ValueKind);
        Assert.Equal("https://api.example.test/health", url.GetProperty("raw").GetString());
        Assert.Equal(JsonValueKind.Array, url.GetProperty("query").ValueKind);
        Assert.False(url.TryGetProperty("variable", out _));
    }

    [Theory]
    [InlineData("""{ "info": { "name": "x" }, "item": [ { "name": "Bad", "request": 42 } ] }""")]
    [InlineData("""{ "info": { "name": "x" }, "item": [ { "name": "Bad", "request": { "url": true } } ] }""")]
    public void NonStringNonObjectForms_StillReportInvalidCollection(string json)
    {
        Assert.Throws<PostmanImportException>(() => _converter.Import(json));
    }
}
