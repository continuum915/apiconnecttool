using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.Services;
using System.Net;
using System.Text.Json;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class QueryParameterSynchronizationTests
{
    [Fact]
    public async Task EmptyQueryDelimiterSurvivesNormalizationAndSending()
    {
        string? sent = null;
        var execution = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        { sent = message.RequestUri!.AbsoluteUri; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); })));
        var tab = RequestTabViewModel.FromRequest(new() { Url = "https://example.test/items?" }, execution);
        await tab.SendCommand.ExecuteAsync(null);
        Assert.Equal("https://example.test/items?", tab.RequestUrl);
        Assert.Equal("https://example.test/items?", sent);
    }
    [Theory]
    [InlineData("%2F", "%252F")]
    [InlineData("a&b", "a%26b")]
    [InlineData("a=b&c=d", "a%3Db%26c%3Dd")]
    [InlineData("a#b", "a%23b")]
    public async Task ChangingOnlyTheBaseUrlPreservesLegacyGridValues(string value, string encoded)
    {
        string? sent = null;
        var execution = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        { sent = message.RequestUri!.AbsoluteUri; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); })));
        var tab = RequestTabViewModel.FromRequest(new() { Url = "https://example.test/old#section", QueryParams = new() { new() { Key = "q", Value = value } } }, execution);
        tab.RequestUrl = tab.RequestUrl.Replace("/old", "/new", StringComparison.Ordinal);
        await tab.SendCommand.ExecuteAsync(null);
        Assert.Equal("https://example.test/new?q=" + encoded + "#section", sent);
        Assert.Equal(value, Assert.Single(tab.CreateSnapshot().QueryParams).Value);
        Assert.Equal("https://example.test/new#section", tab.CreateSnapshot().Url);
    }
    [Fact]
    public async Task VariablesResolveWithoutDoubleEncodingRawUrlValues()
    {
        const string expected = "https://example.test/items?q=%2F&flag&grid=%252F#frag";
        string? sent = null;
        var execution = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        { sent = message.RequestUri!.AbsoluteUri; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); })));
        var environment = new RequestEnvironment { Variables = new() { new() { Key = "route", InitialValue = new("items") }, new() { Key = "value", InitialValue = new("%2F") } } };
        var tab = new RequestTabViewModel(execution, () => environment) { RequestUrl = "https://example.test/{{route}}?q={{value}}&flag#frag" };
        tab.QueryParams.Add(new() { Key = "grid", Value = "{{value}}" });
        await tab.SendCommand.ExecuteAsync(null);
        Assert.True(tab.LastSendSucceeded, tab.ResponseBody);
        Assert.Equal(expected, sent);
        Assert.Contains(expected, tab.BuildCurlCommand(false));
        Assert.Contains("{{value}}", tab.RequestUrl);
    }
    [Fact]
    public void GeneratedAndExportedUrlsKeepRawSpelling()
    {
        var tab = new RequestTabViewModel { RequestUrl = "https://example.test/?raw=%2F&flag#frag" };
        var saved = tab.CreateSnapshot();
        Assert.Contains("https://example.test/?raw=%2F&flag#frag", tab.BuildCurlCommand(false));
        var collection = new RequestCollection { Requests = new() { saved } };
        var exports = new CollectionExportService();
        var imported = exports.Import(exports.Export(collection, Guid.NewGuid(), false));
        Assert.Equal(tab.RequestUrl, RequestTabViewModel.FromRequest(imported.Requests[0]).RequestUrl);
        using var postman = JsonDocument.Parse(new PostmanCollectionConverter().Export(collection, false));
        Assert.Equal("https://example.test/?raw=%2F&flag#frag", postman.RootElement.GetProperty("item")[0].GetProperty("request").GetProperty("url").GetProperty("raw").GetString());
    }
    [Theory]
    [InlineData("https://example.test/items?raw=%2F%20&flag#section", "https://example.test/items?raw=%2F%20&flag&grid=a%20b#section")]
    [InlineData("https://example.test/items?x=1&x=2", "https://example.test/items?x=1&x=2&grid=a%20b")]
    [InlineData("https://example.test/items", "https://example.test/items?grid=a%20b")]
    public async Task ExistingSavedShapesKeepTheirSentUrl(string url, string expected)
    {
        var sent = new List<string>();
        var execution = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(message =>
        { sent.Add(message.RequestUri!.AbsoluteUri); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); })));
        var original = new RequestItem { Url = url, QueryParams = new() { new() { Key = "grid", Value = "a b" }, new() { Key = "off", Value = "skip", IsEnabled = false } } };
        await execution.ExecuteAsync(original);
        var tab = RequestTabViewModel.FromRequest(original, execution);
        await tab.SendCommand.ExecuteAsync(null);
        Assert.Equal(new[] { expected, expected }, sent);
        Assert.Equal(url, original.Url);
    }

    [Fact]
    public void GridEditsAndUrlEditsKeepDisabledRowsAndNotifyDirtyOnce()
    {
        var disabled = new KeyValueItem { Key = "disabled", Value = "saved", IsEnabled = false };
        var tab = RequestTabViewModel.FromRequest(new() { Url = "https://example.test/#fragment", QueryParams = new() { disabled } });
        var dirtyNotifications = 0;
        tab.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(tab.IsDirty) && tab.IsDirty) dirtyNotifications++; };
        tab.RequestUrl = "https://example.test/?one=1#fragment";
        Assert.Contains(tab.QueryParams, row => !row.IsEnabled && row.Key == "disabled");
        Assert.Equal(1, dirtyNotifications);
        tab.MarkSaved(Guid.NewGuid()); dirtyNotifications = 0;
        tab.QueryParams[0].Value = "two words";
        Assert.Equal("https://example.test/?one=two words#fragment", tab.RequestUrl);
        Assert.Equal(1, dirtyNotifications);
        tab.QueryParams[0].IsEnabled = false;
        Assert.Equal("https://example.test/#fragment", tab.RequestUrl);
        tab.QueryParams[0].IsEnabled = true;
        var added = new KeyValueItem { Key = "next", Value = "{{token}}" };
        tab.QueryParams.Add(added);
        Assert.Equal("https://example.test/?one=two words&next={{token}}#fragment", tab.RequestUrl);
        added.Key = "renamed";
        Assert.Contains("renamed={{token}}", tab.RequestUrl);
        tab.QueryParams.Remove(added);
        Assert.Equal("https://example.test/?one=two words#fragment", tab.RequestUrl);
        tab.RequestUrl = "https://example.test/?replacement#fragment";
        Assert.DoesNotContain(tab.QueryParams, row => row.Key == "one");
        Assert.Contains(tab.QueryParams, row => row.Key == "disabled" && !row.IsEnabled);
    }
    [Theory]
    [InlineData("https://example.test/{{route}}?q={{value}}&q=two words#section")]
    [InlineData("https://example.test/?flag&flag=&q=%2F%20%26#frag?not=query")]
    [InlineData("https://example.test/?a=1&&a=2&")]
    [InlineData("https://example.test/#frag?not=query")]
    public void UrlSpellingSurvivesSaveAndReload(string url)
    {
        var tab = new RequestTabViewModel { RequestUrl = url };
        var saved = tab.CreateSnapshot();
        var reopened = RequestTabViewModel.FromRequest(saved);
        Assert.Equal(url, reopened.RequestUrl);
        Assert.Equal(saved.QueryParams.Count, reopened.CreateSnapshot().QueryParams.Count);
        Assert.False(reopened.IsDirty);
    }
    [Fact]
    public void PastingAUrlFillsParamsAndStoresOnlyTheBaseUrl()
    {
        var tab = new RequestTabViewModel();
        var dirtyNotifications = 0;
        tab.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(tab.IsDirty)) dirtyNotifications++; };
        tab.RequestUrl = "https://api.example.test/items?page=2&limit=10";
        var snapshot = tab.CreateSnapshot();
        Assert.Equal("https://api.example.test/items", snapshot.Url);
        Assert.Equal(new[] { "page", "limit" }, snapshot.QueryParams.Select(row => row.Key));
        Assert.Equal(new[] { "2", "10" }, snapshot.QueryParams.Select(row => row.Value));
        Assert.Equal("https://api.example.test/items?page=2&limit=10", tab.RequestUrl);
        Assert.True(tab.IsDirty);
        Assert.Equal(1, dirtyNotifications);
    }
}
