using System.Net;
using System.Text;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public class ResponseViewerFidelityTests
{
    [Fact]
    public void ChangingContentTypeReformatsAnUnchangedBody()
    {
        var tab = new RequestTabViewModel { ResponseBody = "{\"n\":1}" };
        Assert.Equal("{\"n\":1}", tab.DisplayedResponseBody);
        tab.ResponseContentType = "application/json";
        Assert.Contains("\n", tab.DisplayedResponseBody);
    }
    [Fact]
    public async Task PrettyPreservesCharactersAndNumbers_RawAndTestsUseOriginalBody()
    {
        const string body = "  {\"name\":\"café 日本\",\"html\":\"<b>a & b</b>\",\"q\":\"it's\",\"n\":1.50} \n";
        var service = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }))));
        var request = new RequestItem { Url = "https://example.test", TestScript =
            "pm.test('original whitespace', () => pm.expect(pm.response.text().startsWith('  {')).toEqual(true));",
            Assertions = new() { new() { Kind = AssertionKind.Body, Operator = AssertionOperator.Equals, ExpectedValue = body } } };
        var tab = RequestTabViewModel.FromRequest(request, service);
        await tab.SendCommand.ExecuteAsync(null);
        Assert.Equal(body, tab.ResponseBody);
        Assert.Contains("café 日本", tab.DisplayedResponseBody);
        Assert.Contains("<b>a & b</b>", tab.DisplayedResponseBody);
        Assert.Contains("it's", tab.DisplayedResponseBody);
        Assert.Contains("1.50", tab.DisplayedResponseBody);
        Assert.All(tab.AssertionTests.Results, result => Assert.True(result.Passed));
        Assert.Equal(2, tab.AssertionTests.Results.Count);
        tab.IsRawResponse = true;
        Assert.Equal(body, tab.DisplayedResponseBody);
        Assert.Equal(Encoding.UTF8.GetBytes(body), tab.ResponseBytes);
    }

    [Theory]
    [InlineData("{broken", "application/json")]
    [InlineData("<broken", "application/xml")]
    [InlineData("<!DOCTYPE x [<!ENTITY data SYSTEM 'file:///never-read'>]><x>&data;</x>", "application/xml")]
    [InlineData("<!DOCTYPE x [<!ENTITY data 'expanded'>]><x>&data;</x>", "application/xml")]
    public void FormattingFailuresKeepOriginalText(string body, string type)
    {
        var tab = new RequestTabViewModel { ResponseContentType = type, ResponseBody = body };
        Assert.Equal(body, tab.DisplayedResponseBody);
        tab.IsPrettyResponse = false;
        Assert.Equal(body, tab.DisplayedResponseBody);
    }

    [Fact]
    public void InvisibleCharactersAreEscapedInPrettyAndWarnedInBothViews()
    {
        const string body = "{\"value\":\"a\u202Eb\u200Bc\"}";
        var tab = new RequestTabViewModel { ResponseContentType = "application/json", ResponseBody = body };
        Assert.Contains("\\u202E", tab.DisplayedResponseBody);
        Assert.Contains("\\u200B", tab.DisplayedResponseBody);
        Assert.DoesNotContain('\u202E', tab.DisplayedResponseBody);
        Assert.True(tab.HasInvisibleResponseCharacters);
        tab.IsPrettyResponse = false;
        Assert.Equal(body, tab.DisplayedResponseBody);
        Assert.True(tab.HasInvisibleResponseCharacters);
    }

    [Fact]
    public void XmlIsIndentedAndOtherContentRemainsText()
    {
        var tab = new RequestTabViewModel { ResponseContentType = "application/xml", ResponseBody = "<root><child>hello</child></root>" };
        Assert.Contains("\n", tab.DisplayedResponseBody);
        Assert.Contains("<child>hello</child>", tab.DisplayedResponseBody);
        tab.ResponseContentType = "text/plain";
        tab.ResponseBody = "{\"n\":1}";
        Assert.Equal("{\"n\":1}", tab.DisplayedResponseBody);
    }
}
