using ApiConnectTool.Converters;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.Converters;

public class TokenKeysTests
{
    [Theory]
    [InlineData(101, ResponseStatusClass.Informational)]
    [InlineData(200, ResponseStatusClass.Success)]
    [InlineData(201, ResponseStatusClass.Success)]
    [InlineData(302, ResponseStatusClass.Redirect)]
    [InlineData(404, ResponseStatusClass.ClientError)]
    [InlineData(500, ResponseStatusClass.ServerError)]
    public void StatusCodesMapToClasses(int code, ResponseStatusClass expected) =>
        Assert.Equal(expected, ResponseStatusClassifier.FromStatusCode(code));

    [Theory]
    [InlineData(ResponseStatusClass.Success, "ApiConnectTool.Brush.Success")]
    [InlineData(ResponseStatusClass.Redirect, "ApiConnectTool.Brush.Info")]
    [InlineData(ResponseStatusClass.ClientError, "ApiConnectTool.Brush.Danger")]
    [InlineData(ResponseStatusClass.ServerError, "ApiConnectTool.Brush.Danger")]
    [InlineData(ResponseStatusClass.TransportError, "ApiConnectTool.Brush.Danger")]
    [InlineData(ResponseStatusClass.None, "ApiConnectTool.Brush.TextMuted")]
    public void StatusClassesMapToTokens(ResponseStatusClass statusClass, string token) =>
        Assert.Equal(token, TokenKeys.ForStatusClass(statusClass));

    [Theory]
    [InlineData("GET", "ApiConnectTool.Brush.Method.Get")]
    [InlineData("post", "ApiConnectTool.Brush.Method.Post")]
    [InlineData("PUT", "ApiConnectTool.Brush.Method.Put")]
    [InlineData("PATCH", "ApiConnectTool.Brush.Method.Patch")]
    [InlineData("DELETE", "ApiConnectTool.Brush.Method.Delete")]
    [InlineData("HEAD", "ApiConnectTool.Brush.Method.Other")]
    [InlineData("OPTIONS", "ApiConnectTool.Brush.Method.Other")]
    public void MethodsMapToTokens(string method, string token) =>
        Assert.Equal(token, TokenKeys.ForMethod(method));

    [Theory]
    [InlineData(TestRunOutcome.Passed, "ApiConnectTool.Brush.Success")]
    [InlineData(TestRunOutcome.Failed, "ApiConnectTool.Brush.Danger")]
    [InlineData(TestRunOutcome.Incomplete, "ApiConnectTool.Brush.WarningText")]
    [InlineData(TestRunOutcome.None, "ApiConnectTool.Brush.TextMuted")]
    public void TestOutcomesMapToTokens(TestRunOutcome outcome, string token) =>
        Assert.Equal(token, TokenKeys.ForTestOutcome(outcome));

    [Fact]
    public void AssertionResultsMapToPassFailAndIncomplete()
    {
        var pass = new ApiConnectTool.Models.AssertionResult(Guid.NewGuid(), "a", true, "", null);
        var fail = pass with { Passed = false };
        var incomplete = fail with { Incomplete = true };
        Assert.Equal("ApiConnectTool.Brush.Success", TokenKeys.ForAssertionResult(pass));
        Assert.Equal("ApiConnectTool.Brush.Danger", TokenKeys.ForAssertionResult(fail));
        Assert.Equal("ApiConnectTool.Brush.WarningText", TokenKeys.ForAssertionResult(incomplete));
    }
}
