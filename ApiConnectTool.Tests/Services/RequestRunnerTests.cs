using System.Net;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class RequestRunnerTests
{
    internal static RequestRunner Runner(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) =>
        new(new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(callback))));

    [Fact]
    public async Task TestScripts_MergeBeforeAssertionsAndKeepResponse()
    {
        var runner = Runner(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":7}") }));
        var result = await runner.RunAsync(new() { Url = "https://example.test", TestScript =
            "pm.test('id', () => pm.expect(pm.response.json().id).toEqual(7)); pm.test('bad', () => pm.expect(1).toEqual(2));",
            Assertions = new() { new() { Description = "status" } } });
        Assert.True(result.Execution.IsSuccess);
        Assert.Equal(new[] { "id", "bad", "status" }, result.Tests.Select(test => test.Label));
        Assert.Equal(new[] { true, false, true }, result.Tests.Select(test => test.Passed));
    }

    [Fact]
    public async Task PreRequestWrites_SubstituteEveryRequestFieldAndChainWithoutMutatingInput()
    {
        var urls = new List<string>();
        var runner = Runner(async request => {
            urls.Add(request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer fresh", request.Headers.Authorization!.ToString());
            Assert.Equal("fresh", request.Headers.GetValues("X-Value").Single());
            Assert.Equal("fresh", await request.Content!.ReadAsStringAsync());
            return new(HttpStatusCode.OK);
        });
        var original = new Dictionary<string, string> { ["token"] = "old" };
        var request = new RequestItem { Method = HttpMethod.Post, Url = "https://example.test/{{token}}", Body = "{{token}}",
            Headers = new() { new() { Key = "X-Value", Value = "{{token}}" } },
            Auth = new() { Type = AuthType.Bearer, Token = new("{{token}}") },
            PreRequestScript = "pm.variables.set('token', 'fresh');",
            TestScript = "pm.variables.set('next', 'second');" };
        var first = await runner.RunAsync(request, new() { Variables = original, SecretVariableNames = new HashSet<string> { "token" } });
        request.PreRequestScript = null;
        request.Url = "https://example.test/{{next}}";
        await runner.RunAsync(request, new() { Variables = first.Variables, SecretVariableNames = first.SecretVariableNames });
        Assert.Equal(new[] { "https://example.test/fresh", "https://example.test/second" }, urls);
        Assert.Equal("old", original["token"]);
        Assert.Contains("token", first.SecretVariableNames);
        Assert.DoesNotContain("next", first.SecretVariableNames);
    }

    [Fact]
    public async Task PreRequestError_StopsSendAndRedactsOverwrittenSecret()
    {
        var sent = false;
        var runner = Runner(_ => { sent = true; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); });
        var result = await runner.RunAsync(new() { Url = "https://example.test", PreRequestScript =
            "pm.variables.set('token', 'new-secret'); throw new Error(pm.variables.get('token'));" },
            new() { Variables = new Dictionary<string, string> { ["token"] = "old-secret" }, SecretVariableNames = new HashSet<string> { "token" } });
        Assert.False(sent);
        Assert.False(result.Execution.IsSuccess);
        Assert.False(result.TestsEvaluated);
        Assert.DoesNotContain("new-secret", result.Execution.ErrorMessage!);
    }

    [Fact]
    public async Task NoScripts_IsPassThroughAndFailedSendSkipsTests()
    {
        var runner = Runner(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)));
        var plain = await runner.RunAsync(new() { Url = "https://example.test" });
        Assert.Equal(HttpStatusCode.Accepted, plain.Execution.StatusCode);
        Assert.Empty(plain.Tests);
        Assert.Empty(plain.Scripts);
        var failed = await runner.RunAsync(new() { Url = "invalid", TestScript = "throw new Error('must not run');", Assertions = new() { new() } });
        Assert.False(failed.TestsEvaluated);
        Assert.Empty(failed.Tests);
        Assert.Empty(failed.Scripts);
    }

    [Fact]
    public async Task TestScriptError_KeepsResponseAndEvaluatesAssertions()
    {
        var runner = Runner(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var result = await runner.RunAsync(new() { Url = "https://example.test", TestScript = "throw new Error('oops');", Assertions = new() { new() } });
        Assert.True(result.Execution.IsSuccess);
        Assert.Equal(new[] { false, true }, result.Tests.Select(test => test.Passed));
    }

    [Fact]
    public async Task ModifiedBuiltins_CannotBypassOverwrittenSecretRedaction()
    {
        var runner = Runner(_ => throw new InvalidOperationException("must not send"));
        var result = await runner.RunAsync(new() { Url = "https://example.test", PreRequestScript =
            "Set.prototype.has = () => false; pm.variables.set('token', 'replacement'); throw new Error(pm.variables.get('token'));" },
            new() { Variables = new Dictionary<string, string> { ["token"] = "original" }, SecretVariableNames = new HashSet<string> { "token" } });
        Assert.DoesNotContain("replacement", result.Execution.ErrorMessage!);
        Assert.DoesNotContain("replacement", Assert.Single(result.Scripts).ErrorMessage!);
        Assert.DoesNotContain("replacement", Assert.Single(result.Tests).FailureMessage!);
    }

    [Fact]
    public async Task AuthComputedBeforePreRequestError_IsRedacted()
    {
        var runner = Runner(_ => throw new InvalidOperationException("must not send"));
        var result = await runner.RunAsync(new() { Url = "https://example.test", Auth = new() { Type = AuthType.Bearer, Token = new("{{token}}") },
            PreRequestScript = "pm.variables.set('token', 'computed-auth-secret'); pm.test('computed-auth-secret', () => { throw new Error('computed-auth-secret'); }); throw new Error(pm.variables.get('token'));" });
        Assert.DoesNotContain("computed-auth-secret", result.Execution.ErrorMessage!);
        Assert.DoesNotContain("computed-auth-secret", Assert.Single(result.Scripts).ErrorMessage!);
        Assert.All(result.Tests, test => {
            Assert.DoesNotContain("computed-auth-secret", test.Label);
            if (test.FailureMessage != null) Assert.DoesNotContain("computed-auth-secret", test.FailureMessage);
        });
        Assert.DoesNotContain("token", result.SecretVariableNames);
    }
}
