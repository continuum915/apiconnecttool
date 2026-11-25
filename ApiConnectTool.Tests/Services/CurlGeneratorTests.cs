using System.Collections.Generic;
using System.Net.Http;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class CurlGeneratorTests
{
    private static readonly IReadOnlyDictionary<string, string> NoVariables = new Dictionary<string, string>();
    private readonly CurlGenerator _generator = new();

    [Fact]
    public void Generate_SimpleGet_IncludesMethodAndUrl()
    {
        var request = new RequestItem { Method = HttpMethod.Get, Url = "https://api.example.test/pets" };

        var command = _generator.Generate(request, NoVariables, revealSecrets: true);

        Assert.StartsWith("curl -X GET 'https://api.example.test/pets'", command);
    }

    [Fact]
    public void Generate_EnabledQueryParams_AreAppendedToUrlAndEscaped()
    {
        var request = new RequestItem
        {
            Url = "https://api.example.test/pets",
            QueryParams = { new KeyValueItem { Key = "name", Value = "a b&c" } }
        };

        var command = _generator.Generate(request, NoVariables, revealSecrets: true);

        Assert.Contains("name=a%20b%26c", command);
    }

    [Fact]
    public void Generate_DisabledQueryParamsAndHeaders_AreExcluded()
    {
        var request = new RequestItem
        {
            Url = "https://api.example.test/pets",
            QueryParams = { new KeyValueItem { Key = "skip", Value = "1", IsEnabled = false } },
            Headers = { new KeyValueItem { Key = "X-Skip", Value = "1", IsEnabled = false } }
        };

        var command = _generator.Generate(request, NoVariables, revealSecrets: true);

        Assert.DoesNotContain("skip", command);
        Assert.DoesNotContain("X-Skip", command);
    }

    [Fact]
    public void Generate_Headers_EmitDashHFlagPerEnabledHeader()
    {
        var request = new RequestItem
        {
            Url = "https://api.example.test",
            Headers = { new KeyValueItem { Key = "Accept", Value = "application/json" } }
        };

        var command = _generator.Generate(request, NoVariables, revealSecrets: true);

        Assert.Contains("-H 'Accept: application/json'", command);
    }

    [Fact]
    public void Generate_HeaderValueWithSingleQuote_IsShellEscapedCorrectly()
    {
        var request = new RequestItem
        {
            Url = "https://api.example.test",
            Headers = { new KeyValueItem { Key = "X-Note", Value = "it's a test" } }
        };

        var command = _generator.Generate(request, NoVariables, revealSecrets: true);

        Assert.Contains(@"-H 'X-Note: it'\''s a test'", command);
    }

    [Fact]
    public void Generate_RawBodyWithSpecialShellCharacters_IsSafelyQuoted()
    {
        var request = new RequestItem
        {
            Method = HttpMethod.Post,
            Url = "https://api.example.test",
            BodyMode = RequestBodyMode.Raw,
            Body = "{\"name\":\"$(whoami) `id` & echo pwned\"}"
        };

        var command = _generator.Generate(request, NoVariables, revealSecrets: true);

        Assert.Contains("--data-raw '{\"name\":\"$(whoami) `id` & echo pwned\"}'", command);
    }

    [Fact]
    public void Generate_UrlEncodedBody_EncodesEnabledFormFieldsOnly()
    {
        var request = new RequestItem
        {
            Method = HttpMethod.Post,
            Url = "https://api.example.test/token",
            BodyMode = RequestBodyMode.UrlEncoded,
            FormData =
            {
                new KeyValueItem { Key = "grant_type", Value = "client_credentials" },
                new KeyValueItem { Key = "skip", Value = "1", IsEnabled = false }
            }
        };

        var command = _generator.Generate(request, NoVariables, revealSecrets: true);

        Assert.Contains("--data 'grant_type=client_credentials'", command);
        Assert.DoesNotContain("skip", command);
    }

    [Fact]
    public void Generate_FormDataBody_EmitsDashFFlagPerField()
    {
        var request = new RequestItem
        {
            Method = HttpMethod.Post,
            Url = "https://api.example.test/upload",
            BodyMode = RequestBodyMode.FormData,
            FormData = { new KeyValueItem { Key = "name", Value = "Mochi" } }
        };

        var command = _generator.Generate(request, NoVariables, revealSecrets: true);

        Assert.Contains("-F 'name=Mochi'", command);
    }

    [Fact]
    public void Generate_GetMethod_NeverIncludesABodyFlagEvenIfBodyModeIsSet()
    {
        var request = new RequestItem { Method = HttpMethod.Get, Url = "https://api.example.test", BodyMode = RequestBodyMode.Raw, Body = "ignored" };

        var command = _generator.Generate(request, NoVariables, revealSecrets: true);

        Assert.DoesNotContain("--data", command);
    }

    [Fact]
    public void Generate_BearerAuth_EmitsAuthorizationHeader()
    {
        var request = new RequestItem
        {
            Url = "https://api.example.test",
            Auth = new RequestAuth { Type = AuthType.Bearer, Token = "abc123" }
        };

        var command = _generator.Generate(request, NoVariables, revealSecrets: true);

        Assert.Contains("-H 'Authorization: Bearer abc123'", command);
    }

    [Fact]
    public void Generate_BasicAuth_EmitsDashUFlag()
    {
        var request = new RequestItem
        {
            Url = "https://api.example.test",
            Auth = new RequestAuth { Type = AuthType.Basic, Username = "alice", Password = "hunter2" }
        };

        var command = _generator.Generate(request, NoVariables, revealSecrets: true);

        Assert.Contains("-u 'alice:hunter2'", command);
    }

    [Fact]
    public void Generate_ApiKeyAuthAsHeader_EmitsDashHFlag()
    {
        var request = new RequestItem
        {
            Url = "https://api.example.test",
            Auth = new RequestAuth { Type = AuthType.ApiKey, ApiKeyHeaderName = "X-Api-Key", Token = "k-999" }
        };

        var command = _generator.Generate(request, NoVariables, revealSecrets: true);

        Assert.Contains("-H 'X-Api-Key: k-999'", command);
    }

    [Fact]
    public void Generate_ApiKeyAuthAsQueryParam_IsAppendedToUrlNotAsAHeader()
    {
        var request = new RequestItem
        {
            Url = "https://api.example.test/pets",
            Auth = new RequestAuth { Type = AuthType.ApiKey, ApiKeyQueryParam = "api_key", Token = "k-999" }
        };

        var command = _generator.Generate(request, NoVariables, revealSecrets: true);

        Assert.Contains("api_key=k-999", command);
        Assert.DoesNotContain("-H", command);
    }

    [Fact]
    public void Generate_OAuth2Auth_OmitsAuthorizationRatherThanSendingUnauthenticatedSilently()
    {
        var request = new RequestItem
        {
            Url = "https://api.example.test",
            Auth = new RequestAuth { Type = AuthType.OAuth2, ClientId = "c", TokenUrl = "https://auth.example.test/token" }
        };

        var command = _generator.Generate(request, NoVariables, revealSecrets: true);

        Assert.DoesNotContain("Authorization", command);
        Assert.DoesNotContain("-u ", command);
    }

    [Fact]
    public void Generate_VariableTokens_AreSubstitutedFromTheVariableMap()
    {
        var request = new RequestItem { Url = "{{baseUrl}}/pets", Headers = { new KeyValueItem { Key = "X-Trace", Value = "{{traceId}}" } } };
        var variables = new Dictionary<string, string> { ["baseUrl"] = "https://example.test", ["traceId"] = "trace-1" };

        var command = _generator.Generate(request, variables, revealSecrets: true);

        Assert.Contains("'https://example.test/pets'", command);
        Assert.Contains("-H 'X-Trace: trace-1'", command);
    }

    [Fact]
    public void Generate_DefaultRedacted_NeverContainsTheBearerToken()
    {
        var request = new RequestItem
        {
            Url = "https://api.example.test",
            Auth = new RequestAuth { Type = AuthType.Bearer, Token = "top-secret-token" }
        };

        var command = _generator.Generate(request, NoVariables, revealSecrets: false);

        Assert.DoesNotContain("top-secret-token", command);
        Assert.Contains("••••••••", command);
    }

    [Fact]
    public void Generate_RevealSecretsTrue_ContainsTheLiteralToken()
    {
        var request = new RequestItem
        {
            Url = "https://api.example.test",
            Auth = new RequestAuth { Type = AuthType.Bearer, Token = "top-secret-token" }
        };

        var command = _generator.Generate(request, NoVariables, revealSecrets: true);

        Assert.Contains("top-secret-token", command);
    }

    [Fact]
    public void Generate_DefaultRedacted_RedactsBasicPasswordButNotUsername()
    {
        var request = new RequestItem
        {
            Url = "https://api.example.test",
            Auth = new RequestAuth { Type = AuthType.Basic, Username = "alice", Password = "hunter2" }
        };

        var command = _generator.Generate(request, NoVariables, revealSecrets: false);

        Assert.Contains("alice", command);
        Assert.DoesNotContain("hunter2", command);
    }

    [Fact]
    public void Generate_SecretSourcedFromVariable_IsStillRedactedByDefault()
    {
        var request = new RequestItem
        {
            Url = "https://api.example.test",
            Auth = new RequestAuth { Type = AuthType.Bearer, Token = "{{apiToken}}" }
        };
        var variables = new Dictionary<string, string> { ["apiToken"] = "top-secret-token" };

        var command = _generator.Generate(request, variables, revealSecrets: false);

        Assert.DoesNotContain("top-secret-token", command);
    }
}
