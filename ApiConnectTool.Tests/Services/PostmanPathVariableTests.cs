using System.Linq;
using System.Text.Json;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class PostmanPathVariableTests
{
    private readonly PostmanCollectionConverter _converter = new();

    private PostmanImportResult Import(string url, string variablesJson) => _converter.ImportWithReport($$"""
        {
          "info": { "name": "Paths" },
          "item": [
            { "name": "Get user", "request": { "method": "GET", "url": { "raw": {{JsonSerializer.Serialize(url)}}, "variable": {{variablesJson}} } } }
          ]
        }
        """);

    [Fact]
    public void DeclaredValues_AreInlinedAndReported()
    {
        var result = Import("https://api.example.test/users/:userId/orders/:orderId?expand=items",
            """[ { "key": "userId", "value": "42" }, { "key": "orderId", "value": 7 } ]""");

        Assert.Equal("https://api.example.test/users/42/orders/7?expand=items", Assert.Single(result.Collection.Requests).Url);
        var note = Assert.Single(result.Report.Notes);
        Assert.Contains("userId, orderId", note);
        Assert.Contains("Get user", note);
        Assert.Empty(result.Report.Warnings);
    }

    [Fact]
    public void VariableReferenceValues_AreKept()
    {
        var result = Import("{{baseUrl}}/users/:id", """[ { "key": "id", "value": "{{userId}}" } ]""");

        Assert.Equal("{{baseUrl}}/users/{{userId}}", Assert.Single(result.Collection.Requests).Url);
        Assert.Empty(result.Report.Warnings);
    }

    [Theory]
    [InlineData("""[ { "key": "id", "value": "" } ]""")]
    [InlineData("""[ { "key": "id" } ]""")]
    [InlineData("""[ { "key": "id", "value": null } ]""")]
    public void MissingOrEmptyValues_BecomePlaceholdersWithAWarning(string variables)
    {
        var result = Import("{{baseUrl}}/users/:id", variables);

        Assert.Equal("{{baseUrl}}/users/{{id}}", Assert.Single(result.Collection.Requests).Url);
        var warning = Assert.Single(result.Report.Warnings);
        Assert.Contains("\"id\"", warning);
        Assert.Contains("{{id}}", warning);
        Assert.Contains("Get user", warning);
        Assert.Single(result.Report.Notes);
    }

    [Fact]
    public void UndeclaredSegmentsPortsAndOtherColonsAreUnchanged()
    {
        const string url = "http://localhost:8080/v1/:undeclared/items:batch/:id?filter=a:b#frag:id";
        var result = Import(url, """[ { "key": "id", "value": "5" }, { "key": "8080", "value": "x" }, { "key": "b", "value": "x" } ]""");

        Assert.Equal("http://localhost:8080/v1/:undeclared/items:batch/5?filter=a:b#frag:id", Assert.Single(result.Collection.Requests).Url);
    }

    [Fact]
    public void NoVariableArray_LeavesUrlAndReportUntouched()
    {
        var result = _converter.ImportWithReport("""
            { "info": { "name": "Paths" }, "item": [ { "name": "Raw", "request": { "url": { "raw": "https://api.example.test/users/:id" } } } ] }
            """);

        Assert.Equal("https://api.example.test/users/:id", Assert.Single(result.Collection.Requests).Url);
        Assert.Empty(result.Report.Notes);
        Assert.Equal(string.Empty, result.Report.ToUserMessage());
    }

    [Fact]
    public void UserMessage_ListsNotesThenPrefixedWarnings()
    {
        var result = Import("{{baseUrl}}/users/:id/:tab", """[ { "key": "id", "value": "1" }, { "key": "tab", "value": "" } ]""");

        var message = result.Report.ToUserMessage();
        Assert.StartsWith(result.Report.Notes[0], message);
        Assert.Contains("Warning: " + result.Report.Warnings[0], message);
    }

    [Fact]
    public void Export_DoesNotWritePathVariables()
    {
        var collection = Import("{{baseUrl}}/users/:id", """[ { "key": "id", "value": "1" } ]""").Collection;

        using var exported = JsonDocument.Parse(_converter.Export(collection, includeSecrets: false));

        var url = exported.RootElement.GetProperty("item")[0].GetProperty("request").GetProperty("url");
        Assert.Equal("{{baseUrl}}/users/1", url.GetProperty("raw").GetString());
        Assert.False(url.TryGetProperty("variable", out _));
    }
}
