using System.Net;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class AssertionEngineTests
{
    [Theory]
    [InlineData(AssertionOperator.Exists, "", true)]
    [InlineData(AssertionOperator.Equals, "application/json", true)]
    [InlineData(AssertionOperator.Equals, "text/plain", false)]
    [InlineData(AssertionOperator.NotEquals, "text/plain", true)]
    [InlineData(AssertionOperator.NotEquals, "application/json", false)]
    [InlineData(AssertionOperator.Contains, "json", true)]
    [InlineData(AssertionOperator.Contains, "xml", false)]
    [InlineData(AssertionOperator.Matches, "^application/.*$", true)]
    [InlineData(AssertionOperator.Matches, "^text/", false)]
    public void Headers_CompareValuesUsingCaseInsensitiveNames(AssertionOperator op, string expected, bool passed)
    {
        var result = Evaluate(new() { Kind = AssertionKind.Header, Target = "content-type", Operator = op, ExpectedValue = expected });
        Assert.Equal(passed, result.Passed);
        Assert.Equal("application/json", result.ActualValue);
        if (!passed) Assert.Contains("content-type", result.FailureMessage!);
    }

    [Theory]
    [InlineData(AssertionOperator.GreaterThan, "12", true)]
    [InlineData(AssertionOperator.GreaterThan, "12.5", false)]
    [InlineData(AssertionOperator.GreaterThan, "13", false)]
    [InlineData(AssertionOperator.LessThan, "13", true)]
    [InlineData(AssertionOperator.LessThan, "12.5", false)]
    [InlineData(AssertionOperator.LessThan, "12", false)]
    [InlineData(AssertionOperator.LessThan, "oops", false)]
    [InlineData(AssertionOperator.GreaterThan, "NaN", false)]
    [InlineData(AssertionOperator.LessThan, "Infinity", false)]
    public void ResponseTime_UsesFractionalMillisecondsAndStrictBoundaries(AssertionOperator op, string expected, bool passed)
    {
        var result = Evaluate(new() { Kind = AssertionKind.ResponseTime, Operator = op, ExpectedValue = expected });
        Assert.Equal(passed, result.Passed);
        Assert.Equal("12.5", result.ActualValue);
        if (!passed) Assert.NotEmpty(result.FailureMessage!);
        if (expected is "oops" or "NaN" or "Infinity") Assert.Contains("must be a number", result.FailureMessage!);
    }

    [Theory]
    [InlineData(AssertionOperator.Exists)]
    [InlineData(AssertionOperator.Equals)]
    [InlineData(AssertionOperator.NotEquals)]
    [InlineData(AssertionOperator.Contains)]
    [InlineData(AssertionOperator.Matches)]
    public void MissingHeaders_FailEvenForNotEquals(AssertionOperator op)
    {
        var result = Evaluate(new() { Kind = AssertionKind.Header, Target = "X-Missing", Operator = op });
        Assert.False(result.Passed);
        Assert.Equal("Header \"X-Missing\" is missing.", result.FailureMessage);
        Assert.Equal("", result.ActualValue);
    }

    [Fact]
    public void MalformedRegex_FailsWithAnExplanation()
    {
        var result = Evaluate(new() { Kind = AssertionKind.Header, Target = "Content-Type", Operator = AssertionOperator.Matches, ExpectedValue = "[" });
        Assert.False(result.Passed);
        Assert.Contains("not a valid regular expression", result.FailureMessage!);
    }

    [Theory]
    [InlineData(AssertionKind.StatusCode, AssertionOperator.Equals, "", "200", "status equals 200")]
    [InlineData(AssertionKind.Header, AssertionOperator.Contains, "Content-Type", "json", "header Content-Type contains json")]
    [InlineData(AssertionKind.Header, AssertionOperator.Exists, "Content-Type", "ignored", "header Content-Type exists")]
    [InlineData(AssertionKind.ResponseTime, AssertionOperator.LessThan, "", "20", "response time (ms) less than 20")]
    public void BlankDescriptions_GenerateKindSpecificLabels(AssertionKind kind, AssertionOperator op, string target, string expected, string label)
    {
        Assert.Equal(label, Evaluate(new() { Kind = kind, Operator = op, Target = target, ExpectedValue = expected, Description = " " }).Label);
    }

    [Fact]
    public void HeaderResults_RedactSecretsAfterComparingOriginalValues()
    {
        var assertion = new Assertion { Kind = AssertionKind.Header, Target = "Content-Type", ExpectedValue = "application/json" };
        var passed = Evaluate(assertion, "application/json");
        Assert.True(passed.Passed);
        Assert.DoesNotContain("application/json", passed.ActualValue);
        Assert.DoesNotContain("application/json", passed.Label);
        assertion.ExpectedValue = "json";
        var failed = Evaluate(assertion, "application/json", "json");
        Assert.False(failed.Passed);
        Assert.DoesNotContain("json", failed.FailureMessage!);
        assertion.Description = "json description";
        Assert.DoesNotContain("json", Evaluate(assertion, "json").Label);
    }

    [Theory]
    [InlineData(AssertionKind.Header, AssertionOperator.GreaterThan)]
    [InlineData(AssertionKind.ResponseTime, AssertionOperator.Equals)]
    [InlineData(AssertionKind.StatusCode, AssertionOperator.Contains)]
    public void UnsupportedCombinations_FailWithAnExplanation(AssertionKind kind, AssertionOperator op)
    {
        var result = Evaluate(new() { Kind = kind, Operator = op, Target = "Content-Type" });
        Assert.False(result.Passed);
        Assert.Equal("Unsupported operator for this assertion kind.", result.FailureMessage);
    }

    [Fact]
    public void Body_ContainsDecodedText()
    {
        var result = Assert.Single(new AssertionEngine().Evaluate(
            [new() { Kind = AssertionKind.Body, Operator = AssertionOperator.Contains, ExpectedValue = "café" }],
            new() { IsSuccess = true, StatusCode = HttpStatusCode.OK,
                Body = System.Text.Encoding.Latin1.GetBytes("un café"), CharacterSet = "iso-8859-1" }));
        Assert.True(result.Passed);
        Assert.Equal("un café", result.ActualValue);
    }

    [Theory]
    [InlineData(AssertionOperator.Contains, "café", true)]
    [InlineData(AssertionOperator.Contains, "CAFÉ", false)]
    [InlineData(AssertionOperator.Equals, "un café", true)]
    [InlineData(AssertionOperator.Equals, "café", false)]
    [InlineData(AssertionOperator.Matches, "^un .+$", true)]
    [InlineData(AssertionOperator.Matches, "^other", false)]
    [InlineData(AssertionOperator.Matches, "[", false)]
    [InlineData(AssertionOperator.Exists, "", false)]
    [InlineData(AssertionOperator.NotEquals, "other", false)]
    [InlineData(AssertionOperator.GreaterThan, "1", false)]
    [InlineData(AssertionOperator.LessThan, "1", false)]
    public void Body_OperatorTable(AssertionOperator op, string expected, bool passed)
    {
        var result = EvaluateBody(AssertionKind.Body, op, "", expected, "un café");
        Assert.Equal(passed, result.Passed);
        Assert.Equal("un café", result.ActualValue);
        if (!passed) Assert.NotEmpty(result.FailureMessage!);
    }

    [Theory]
    [InlineData(AssertionOperator.Equals, "42", true)]
    [InlineData(AssertionOperator.Equals, "43", false)]
    [InlineData(AssertionOperator.NotEquals, "43", true)]
    [InlineData(AssertionOperator.NotEquals, "42", false)]
    [InlineData(AssertionOperator.Contains, "2", true)]
    [InlineData(AssertionOperator.Contains, "3", false)]
    [InlineData(AssertionOperator.GreaterThan, "41.5", true)]
    [InlineData(AssertionOperator.GreaterThan, "42", false)]
    [InlineData(AssertionOperator.LessThan, "42.5", true)]
    [InlineData(AssertionOperator.LessThan, "42", false)]
    [InlineData(AssertionOperator.Exists, "", true)]
    [InlineData(AssertionOperator.Matches, ".*", false)]
    [InlineData(AssertionOperator.GreaterThan, "NaN", false)]
    [InlineData(AssertionOperator.LessThan, "Infinity", false)]
    [InlineData(AssertionOperator.LessThan, "oops", false)]
    public void JsonPath_OperatorTable(AssertionOperator op, string expected, bool passed)
    {
        var result = EvaluateBody(AssertionKind.JsonPath, op, "data.items[0].id", expected,
            """{"data":{"items":[{"id":42}]}}""");
        Assert.Equal(passed, result.Passed);
        Assert.Equal("42", result.ActualValue);
        if (!passed) Assert.NotEmpty(result.FailureMessage!);
    }

    [Theory]
    [InlineData("", "Invalid JSON path")]
    [InlineData(".data", "Invalid JSON path")]
    [InlineData("data.", "Invalid JSON path")]
    [InlineData("data..id", "Invalid JSON path")]
    [InlineData("data[", "Invalid JSON path")]
    [InlineData("data[-1]", "Invalid JSON path")]
    [InlineData("data[1.5]", "Invalid JSON path")]
    [InlineData("data[*]", "Invalid JSON path")]
    [InlineData("data[999999999999999]", "Invalid JSON path")]
    [InlineData("data[0]id", "Invalid JSON path")]
    [InlineData("missing.[0]", "Invalid JSON path")]
    [InlineData("missing", "is missing")]
    [InlineData("data[0]", "is missing")]
    [InlineData("data.id.name", "is missing")]
    [InlineData("items[5]", "is missing")]
    public void JsonPath_InvalidOrAbsentPathsFail(string path, string message)
    {
        foreach (var op in new[] { AssertionOperator.Exists, AssertionOperator.NotEquals })
        {
            var result = EvaluateBody(AssertionKind.JsonPath, op, path, "anything", """{"data":{"id":1},"items":[]}""");
            Assert.False(result.Passed);
            Assert.Contains(message, result.FailureMessage!);
        }
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("{")]
    public void JsonPath_NonJsonFails(string body)
    {
        var result = EvaluateBody(AssertionKind.JsonPath, AssertionOperator.Exists, "data", "", body);
        Assert.False(result.Passed);
        Assert.Equal("response body is not valid JSON", result.FailureMessage);
    }

    [Theory]
    [InlineData("null", "null")]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    [InlineData("1.25", "1.25")]
    [InlineData("\"hello\"", "hello")]
    [InlineData("[]", "[]")]
    [InlineData("{}", "{}")]
    public void JsonPath_ValuesAreReadableAndNullExists(string json, string actual)
    {
        var body = "{\"value\":" + json + "}";
        Assert.True(EvaluateBody(AssertionKind.JsonPath, AssertionOperator.Exists, "value", "", body).Passed);
        var result = EvaluateBody(AssertionKind.JsonPath, AssertionOperator.Equals, "value", actual, body);
        Assert.True(result.Passed);
        Assert.Equal(actual, result.ActualValue);
    }

    [Fact]
    public void JsonPath_RootAndNestedArrayIndexesWork()
    {
        Assert.True(EvaluateBody(AssertionKind.JsonPath, AssertionOperator.Equals, "[0][1].id", "7",
            """[[{"id":6},{"id":7}]]""").Passed);
    }

    [Theory]
    [InlineData("\"42\"")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("1e400")]
    public void JsonPath_NumericComparisonRequiresFiniteJsonNumbers(string json)
    {
        var result = EvaluateBody(AssertionKind.JsonPath, AssertionOperator.GreaterThan, "value", "1", "{\"value\":" + json + "}");
        Assert.False(result.Passed);
        Assert.Contains("must be finite numbers", result.FailureMessage!);
    }

    [Theory]
    [InlineData(AssertionKind.Body, "", "secret")]
    [InlineData(AssertionKind.JsonPath, "token", "{\"token\":\"secret\"}")]
    public void BodyAndJsonPath_RedactAfterComparison(AssertionKind kind, string path, string body)
    {
        var passed = EvaluateBody(kind, AssertionOperator.Equals, path, "secret", body, "secret");
        Assert.True(passed.Passed);
        Assert.DoesNotContain("secret", passed.ActualValue);
        Assert.DoesNotContain("secret", passed.Label);
        var failed = EvaluateBody(kind, AssertionOperator.Equals, path, "other", body, "secret");
        Assert.False(failed.Passed);
        Assert.DoesNotContain("secret", failed.FailureMessage!);
        var invalid = EvaluateBody(AssertionKind.JsonPath, AssertionOperator.Exists, "secret.", "", body, "secret");
        Assert.DoesNotContain("secret", invalid.FailureMessage!);
    }

    [Fact]
    public void Body_InvalidCharsetFallsBackToUtf8()
    {
        var result = Assert.Single(new AssertionEngine().Evaluate(
            [new() { Kind = AssertionKind.Body, ExpectedValue = "café" }],
            new() { IsSuccess = true, StatusCode = HttpStatusCode.OK,
                Body = System.Text.Encoding.UTF8.GetBytes("café"), CharacterSet = "invalid-charset" }));
        Assert.True(result.Passed);
    }

    [Theory]
    [InlineData(AssertionOperator.GreaterThan, "9007199254740993", "9007199254740992")]
    [InlineData(AssertionOperator.LessThan, "9007199254740992", "9007199254740993")]
    [InlineData(AssertionOperator.GreaterThan, "1.0000000000000000001", "1")]
    [InlineData(AssertionOperator.GreaterThan, "1e-30", "0")]
    [InlineData(AssertionOperator.LessThan, "0", "1e-30")]
    public void JsonPath_NumericComparisonsPreserveDecimalPrecision(AssertionOperator op, string actual, string expected)
    {
        Assert.True(EvaluateBody(AssertionKind.JsonPath, op, "value", expected, "{\"value\":" + actual + "}").Passed);
    }

    [Fact]
    public void Schema_MatchesNestedRequiredPropertiesAndArrayItems()
    {
        var result = EvaluateBody(AssertionKind.Schema, AssertionOperator.Equals,
            """{"type":"object","required":["users"],"properties":{"users":{"type":"array","items":{"type":"object","required":["name"],"properties":{"name":{"type":"string","enum":["Ada"]}}}}}}""",
            "", """{"users":[{"name":"Ada"}]}""");
        Assert.True(result.Passed, result.FailureMessage);
    }

    [Theory]
    [InlineData("""{"type":"string"}""", "42", "$", "type string")]
    [InlineData("""{"required":["id"]}""", "{}", """$["id"]""", "required")]
    [InlineData("""{"enum":["Ada"]}""", "\"Grace\"", "$", "enum")]
    [InlineData("""{"properties":{"user":{"properties":{"id":{"type":"integer"}}}}}""", """{"user":{"id":1.5}}""", """$["user"]["id"]""", "integer")]
    [InlineData("""{"items":{"type":"boolean"}}""", "[true,0]", "$[1]", "boolean")]
    public void Schema_MismatchesNameLocationAndReason(string schema, string body, string location, string reason)
    {
        var result = EvaluateBody(AssertionKind.Schema, AssertionOperator.Equals, schema, "", body);
        Assert.False(result.Passed);
        Assert.Contains(location, result.FailureMessage!);
        Assert.Contains(reason, result.FailureMessage!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"type":"unknown"}""")]
    [InlineData("""{"type":["null","string"]}""")]
    [InlineData("""{"properties":[]}""")]
    [InlineData("""{"properties":{"absent":{"type":7}}}""")]
    [InlineData("""{"required":true}""")]
    [InlineData("""{"required":[1]}""")]
    [InlineData("""{"enum":[]}""")]
    [InlineData("""{"enum":1}""")]
    [InlineData("""{"items":[]}""")]
    public void Schema_InvalidDefinitionsFailWithoutThrowing(string schema)
    {
        var result = EvaluateBody(AssertionKind.Schema, AssertionOperator.Equals, schema, "", "{}");
        Assert.False(result.Passed);
        Assert.Contains("Invalid schema", result.FailureMessage!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{")]
    public void Schema_NonJsonBodyFails(string body)
    {
        var result = EvaluateBody(AssertionKind.Schema, AssertionOperator.Equals, "{}", "", body);
        Assert.False(result.Passed);
        Assert.Equal("response body is not valid JSON", result.FailureMessage);
    }

    [Theory]
    [InlineData("object", "{}", true)]
    [InlineData("array", "[]", true)]
    [InlineData("string", "\"text\"", true)]
    [InlineData("number", "1.5", true)]
    [InlineData("integer", "1.0", true)]
    [InlineData("integer", "1e400", true)]
    [InlineData("integer", "1e-400", false)]
    [InlineData("integer", "1.0000000000000000001", false)]
    [InlineData("integer", "100e-2", true)]
    [InlineData("integer", "0e-400", true)]
    [InlineData("boolean", "false", true)]
    [InlineData("null", "null", true)]
    [InlineData("string", "null", false)]
    public void Schema_TypesUseJsonValues(string type, string body, bool passed)
    {
        Assert.Equal(passed, EvaluateBody(AssertionKind.Schema, AssertionOperator.Equals,
            "{\"type\":\"" + type + "\"}", "", body).Passed);
    }

    [Theory]
    [InlineData("""{"enum":[{"a":1,"b":[true,null]}]}""", """{"b":[true,null],"a":1.0}""")]
    [InlineData("""{"minimum":100,"additionalProperties":false,"$ref":"ignored","properties":{"absent":{"type":"string"}}}""", """{"extra":1}""")]
    [InlineData("""{"required":["id"],"properties":{"id":{"type":"number"}},"items":{"type":"string"}}""", "null")]
    public void Schema_UsesStructuralEnumEqualityAndIgnoresUnsupportedKeywords(string schema, string body)
    {
        Assert.True(EvaluateBody(AssertionKind.Schema, AssertionOperator.Equals, schema, "", body).Passed);
    }

    [Fact]
    public void Schema_DeepNestingHasNoFixedDepthLimit()
    {
        var schema = """{"type":"integer"}""";
        var body = "1";
        for (var depth = 0; depth < 200; depth++)
        {
            schema = "{\"items\":" + schema + "}";
            body = "[" + body + "]";
        }
        Assert.True(EvaluateBody(AssertionKind.Schema, AssertionOperator.Equals, schema, "", body).Passed);
    }

    [Fact]
    public void Schema_ResultsRedactAfterComparing()
    {
        var schema = """{"properties":{"secret":{"enum":["secret"]}}}""";
        var body = """{"secret":"secret"}""";
        var passed = EvaluateBody(AssertionKind.Schema, AssertionOperator.Equals, schema, "", body, "secret");
        Assert.True(passed.Passed);
        Assert.DoesNotContain("secret", passed.ActualValue);
        var failed = EvaluateBody(AssertionKind.Schema, AssertionOperator.Equals, schema, "", """{"secret":"other"}""", "secret");
        Assert.False(failed.Passed);
        Assert.DoesNotContain("secret", failed.FailureMessage!);
    }

    private static AssertionResult EvaluateBody(AssertionKind kind, AssertionOperator op, string path, string expected, string body, params string[] secrets) =>
        Assert.Single(new AssertionEngine().Evaluate(
            [new() { Kind = kind, Operator = op, Target = path, ExpectedValue = expected }],
            new() { IsSuccess = true, StatusCode = HttpStatusCode.OK,
                Body = System.Text.Encoding.UTF8.GetBytes(body), ResolvedSecretValues = secrets }));

    private static AssertionResult Evaluate(Assertion assertion, params string[] secrets) => Assert.Single(new AssertionEngine().Evaluate([assertion], new()
    {
        IsSuccess = true, StatusCode = HttpStatusCode.OK, Elapsed = TimeSpan.FromMilliseconds(12.5),
        Headers = [new() { Key = "Content-Type", Value = "application/json" }], ResolvedSecretValues = secrets
    }));
}
