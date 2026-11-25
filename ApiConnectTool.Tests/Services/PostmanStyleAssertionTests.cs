using System.Net;
using System.Text;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

/// <summary>pm.response.code, pm.response.to.* and the Chai-style pm.expect subset.</summary>
public class PostmanStyleAssertionTests
{
    private static RequestExecutionResult Response(string body = """{"id":7,"name":"Rex","tags":["a","b"]}""", bool truncated = false) => new()
    {
        IsSuccess = true,
        StatusCode = HttpStatusCode.OK,
        Body = Encoding.UTF8.GetBytes(body),
        BodyWasTruncated = truncated,
        Headers = [new KeyValueItem { Key = "Content-Type", Value = "application/json" }]
    };

    private static ScriptRunResult Run(string source, RequestExecutionResult? response = null) =>
        new ScriptSandbox().Run(source, new() { Url = "https://example.test" }, response ?? Response(),
            new Dictionary<string, string>(), new HashSet<string>(), []);

    private static AssertionResult Single(string assertion, RequestExecutionResult? response = null)
    {
        var run = Run($"pm.test('t', () => {{ {assertion}; }});", response);
        Assert.Equal(ScriptOutcome.Completed, run.Outcome);
        return Assert.Single(run.Tests);
    }

    [Fact]
    public void ResponseCodeIsTheNumericStatus_AndStatusStaysNumeric()
    {
        Assert.True(Single("pm.expect(pm.response.code).to.equal(200)").Passed);
        Assert.True(Single("pm.expect(typeof pm.response.status).to.equal('number')").Passed);
        Assert.True(Single("pm.expect(pm.response.status).to.equal(pm.response.code)").Passed);
    }

    [Theory]
    // Each passes as written and fails with `.not` inserted, and the reverse.
    [InlineData("pm.expect(5).to.equal(5)", "pm.expect(5).to.not.equal(5)")]
    [InlineData("pm.expect('5').to.not.equal(5)", "pm.expect('5').to.equal(5)")]
    [InlineData("pm.expect({a: [1, {b: 2}]}).to.eql({a: [1, {b: 2}]})", "pm.expect({a: [1, {b: 2}]}).to.not.eql({a: [1, {b: 2}]})")]
    [InlineData("pm.expect({a: 1}).to.not.eql({a: 1, b: 2})", "pm.expect({a: 1}).to.eql({a: 1, b: 2})")]
    [InlineData("pm.expect('hello world').to.include('world')", "pm.expect('hello world').to.not.include('world')")]
    [InlineData("pm.expect([1, 2, 3]).to.include(2)", "pm.expect([1, 2, 3]).not.to.include(2)")]
    [InlineData("pm.expect({a: 1, b: 2}).to.include({a: 1})", "pm.expect({a: 1, b: 2}).to.not.include({a: 1})")]
    [InlineData("pm.expect([1]).to.not.include(5)", "pm.expect([1]).to.include(5)")]
    [InlineData("pm.expect({id: 1}).to.have.property('id')", "pm.expect({id: 1}).to.not.have.property('id')")]
    [InlineData("pm.expect({id: 1}).to.have.property('id', 1)", "pm.expect({id: 1}).to.not.have.property('id', 1)")]
    [InlineData("pm.expect({id: 1}).to.not.have.property('id', 2)", "pm.expect({id: 1}).to.have.property('id', 2)")]
    [InlineData("pm.expect(3).to.be.above(2)", "pm.expect(3).to.not.be.above(2)")]
    [InlineData("pm.expect(1).to.not.be.above(2)", "pm.expect(1).to.be.above(2)")]
    [InlineData("pm.expect(1).to.be.below(2)", "pm.expect(1).to.not.be.below(2)")]
    [InlineData("pm.expect(0).to.exist", "pm.expect(0).to.not.exist")]
    [InlineData("pm.expect(null).to.not.exist", "pm.expect(undefined).to.exist")]
    [InlineData("pm.expect('x').to.be.ok", "pm.expect('x').to.not.be.ok")]
    [InlineData("pm.expect(0).to.not.be.ok", "pm.expect('').to.be.ok")]
    [InlineData("pm.response.to.have.status(200)", "pm.response.to.not.have.status(200)")]
    [InlineData("pm.response.to.not.have.status(404)", "pm.response.to.have.status(404)")]
    [InlineData("pm.response.to.have.header('content-type')", "pm.response.to.not.have.header('Content-Type')")]
    [InlineData("pm.response.to.have.header('Content-Type', 'application/json')", "pm.response.to.have.header('Content-Type', 'text/plain')")]
    [InlineData("pm.response.to.not.have.header('X-Missing')", "pm.response.to.have.header('X-Missing')")]
    [InlineData("pm.response.to.be.ok", "pm.response.to.not.be.ok")]
    [InlineData("pm.response.to.be.success", "pm.response.to.not.be.success")]
    [InlineData("pm.response.to.have.body()", "pm.response.to.not.have.body()")]
    public void EachMemberPassesAndFailsWithAndWithoutNot(string passing, string failing)
    {
        var passed = Single(passing); Assert.True(passed.Passed, passed.FailureMessage);
        var failed = Single(failing);
        Assert.False(failed.Passed);
        Assert.StartsWith("Error: Expected ", failed.FailureMessage);
    }

    [Fact]
    public void FailureMessagesNameTheValuesAndTheNegation()
    {
        Assert.Equal("Error: Expected 1 to equal 2.", Single("pm.expect(1).to.equal(2)").FailureMessage);
        Assert.Equal("Error: Expected 1 not to equal 1.", Single("pm.expect(1).to.not.equal(1)").FailureMessage);
        Assert.Equal("Error: Expected response with status 200 to have status 404.", Single("pm.response.to.have.status(404)").FailureMessage);
        Assert.Equal("Error: Expected null to exist.", Single("pm.expect(null).to.exist").FailureMessage);
    }

    [Fact]
    public void LanguageChainsAndChainedMatchersWork()
    {
        Assert.Contains("'an' is not supported", Single("pm.expect(pm.response.json()).to.be.an.ok").FailureMessage);
        Assert.True(Single("pm.expect(2).to.be.above(1).and.be.below(3)").Passed);
        Assert.True(Single("pm.expect(pm.response.json().tags).which.does.include('a')").Passed);
        Assert.True(Single("pm.expect(pm.response.json()).to.have.property('name', 'Rex')").Passed);
    }

    [Theory]
    [InlineData("pm.expect('x').to.be.a('string')", "a")]
    [InlineData("pm.expect(true).to.be.true", "true")]
    [InlineData("pm.expect([1]).to.have.lengthOf(1)", "lengthOf")]
    [InlineData("pm.response.to.have.jsonBody('id')", "jsonBody")]
    [InlineData("pm.expect(1).not.to.be.empty", "empty")]
    public void UnsupportedMembersFailWithANotSupportedMessage(string assertion, string member)
    {
        var result = Single(assertion);
        Assert.False(result.Passed);
        Assert.Contains($"The assertion '{member}' is not supported.", result.FailureMessage);
        Assert.DoesNotContain("TypeError", result.FailureMessage);
    }

    [Fact]
    public void NumericComparisonsRejectNonNumbersEvenWhenNegated()
    {
        Assert.Contains("Expected numbers to compare", Single("pm.expect('3').to.be.above(2)").FailureMessage);
        Assert.False(Single("pm.expect('3').to.not.be.above(2)").Passed);
    }

    [Fact]
    public void ExistingMatchersKeepWorking()
    {
        Assert.True(Single("pm.expect(pm.response.json()).toEqual({id: 7, name: 'Rex', tags: ['a', 'b']})").Passed);
        Assert.True(Single("pm.expect('abc').toContain('b')").Passed);
        Assert.True(Single("pm.expect(3).toBeGreaterThan(2)").Passed);
        Assert.True(Single("pm.expect(1).toBeLessThan(2)").Passed);
        Assert.True(Single("pm.expect(1).toExist()").Passed);
        Assert.Equal("Error: Expectation did not match.", Single("pm.expect(1).toEqual(2)").FailureMessage);
    }

    [Theory]
    [InlineData("pm.response.to.have.body('{}')")]
    [InlineData("pm.response.to.not.have.body()")]
    [InlineData("pm.expect(pm.response.json()).to.have.property('id')")]
    [InlineData("pm.expect(pm.response.text()).to.not.include('secret')")]
    public void BodyReadsOfATruncatedResponseAreIncompleteNeverPassed(string assertion)
    {
        var result = Single(assertion, Response(truncated: true));
        Assert.False(result.Passed);
        Assert.True(result.Incomplete);
    }

    [Fact]
    public void ChainsCannotBeModifiedByScripts()
    {
        var run = Run("const chain = pm.expect(1); try { chain.equal = () => {}; } catch (e) {} pm.test('t', () => pm.expect(1).to.equal(2));");
        Assert.False(Assert.Single(run.Tests).Passed);
    }

    [Fact]
    public async Task SingleSendsAndCollectionRunsGiveIdenticalResults()
    {
        const string script = """
            pm.test('code', () => pm.expect(pm.response.code).to.equal(201));
            pm.test('status', () => pm.response.to.have.status(201));
            pm.test('header', () => pm.response.to.have.header('X-Id', '7'));
            pm.test('json', () => pm.expect(pm.response.json()).to.have.property('id', 7));
            pm.test('not', () => pm.expect(pm.response.json().id).to.not.be.above(100));
            pm.test('fails', () => pm.expect(pm.response.json()).to.eql({id: 8}));
            pm.test('unsupported', () => pm.expect(1).to.be.a('number'));
            """;
        Task<HttpResponseMessage> Respond(HttpRequestMessage _)
        {
            var response = new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{\"id\":7}") };
            response.Headers.Add("X-Id", "7");
            return Task.FromResult(response);
        }
        var request = new RequestItem { Name = "r", Url = "https://example.test", TestScript = script };

        var single = await RequestRunnerTests.Runner(Respond).RunAsync(request);
        var report = await new CollectionRunner(RequestRunnerTests.Runner(Respond)).RunAsync(new RequestCollection { Requests = { request } });
        var run = Assert.Single(report.Entries).Tests;

        Assert.Equal(new[] { true, true, true, true, true, false, false }, single.Tests.Select(test => test.Passed));
        Assert.Equal(single.Tests.Select(test => (test.Label, test.Passed, test.FailureMessage)),
            run.Select(test => (test.Label, test.Passed, test.FailureMessage)));
    }
}
