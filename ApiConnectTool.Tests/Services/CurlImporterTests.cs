using System.Collections.Generic;
using System.Net.Http;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class CurlImporterTests
{
    private readonly CurlImporter _importer = new();

    [Fact]
    public void Import_SimpleGet_DefaultsToGetMethod()
    {
        var request = _importer.Import("curl https://api.example.test/pets");

        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.example.test/pets", request.Url);
    }

    [Fact]
    public void Import_ExplicitMethodFlag_SetsMethod()
    {
        var request = _importer.Import("curl -X DELETE https://api.example.test/pets/1");

        Assert.Equal(HttpMethod.Delete, request.Method);
    }

    [Fact]
    public void Import_LongRequestFlag_SetsMethod()
    {
        var request = _importer.Import("curl --request PUT https://api.example.test/pets/1");

        Assert.Equal(HttpMethod.Put, request.Method);
    }

    [Fact]
    public void Import_HeaderFlag_AddsHeaderRow()
    {
        var request = _importer.Import("curl -H 'Accept: application/json' https://api.example.test");

        var header = Assert.Single(request.Headers);
        Assert.Equal("Accept", header.Key);
        Assert.Equal("application/json", header.Value);
    }

    [Fact]
    public void Import_LongHeaderFlag_AddsHeaderRow()
    {
        var request = _importer.Import("curl --header 'X-Trace: abc' https://api.example.test");

        Assert.Equal("abc", request.Headers[0].Value);
    }

    [Fact]
    public void Import_AuthorizationBearerHeader_IsFoldedIntoAuthNotLeftAsPlainHeader()
    {
        var request = _importer.Import("curl -H 'Authorization: Bearer abc123' https://api.example.test");

        Assert.Equal(AuthType.Bearer, request.Auth.Type);
        Assert.Equal("abc123", request.Auth.Token.Value);
        Assert.Empty(request.Headers);
    }

    [Fact]
    public void Import_DataRawFlag_SetsRawBodyAndImpliesPost()
    {
        var request = _importer.Import("curl --data-raw '{\"name\":\"Mochi\"}' https://api.example.test/pets");

        Assert.Equal(RequestBodyMode.Raw, request.BodyMode);
        Assert.Equal("{\"name\":\"Mochi\"}", request.Body);
        Assert.Equal(HttpMethod.Post, request.Method);
    }

    [Fact]
    public void Import_DataFlagWithKeyValuePairs_ParsesAsUrlEncodedFormData()
    {
        var request = _importer.Import("curl -d 'grant_type=client_credentials&scope=read' https://api.example.test/token");

        Assert.Equal(RequestBodyMode.UrlEncoded, request.BodyMode);
        Assert.Equal(2, request.FormData.Count);
        Assert.Equal("client_credentials", request.FormData[0].Value);
        Assert.Equal("read", request.FormData[1].Value);
    }

    [Fact]
    public void Import_DataFlagWithJsonBody_ParsesAsRawNotFormData()
    {
        var request = _importer.Import("curl -d '{\"a\":1}' https://api.example.test");

        Assert.Equal(RequestBodyMode.Raw, request.BodyMode);
        Assert.Equal("{\"a\":1}", request.Body);
        Assert.Empty(request.FormData);
    }

    [Fact]
    public void Import_DataFlagUrlEncodesPercentEscapedValues()
    {
        var request = _importer.Import("curl -d 'name=a%20b' https://api.example.test");

        Assert.Equal("a b", request.FormData[0].Value);
    }

    [Fact]
    public void Import_DataBinaryWithAtFile_SetsBinaryBodyModeAndFilePath()
    {
        var request = _importer.Import("curl --data-binary @/tmp/payload.bin https://api.example.test");

        Assert.Equal(RequestBodyMode.Binary, request.BodyMode);
        Assert.Equal("/tmp/payload.bin", request.BodyFilePath);
    }

    [Fact]
    public void Import_UserFlag_SetsBasicAuth()
    {
        var request = _importer.Import("curl -u alice:hunter2 https://api.example.test");

        Assert.Equal(AuthType.Basic, request.Auth.Type);
        Assert.Equal("alice", request.Auth.Username);
        Assert.Equal("hunter2", request.Auth.Password.Value);
    }

    [Fact]
    public void Import_LongUserFlag_SetsBasicAuth()
    {
        var request = _importer.Import("curl --user alice:hunter2 https://api.example.test");

        Assert.Equal(AuthType.Basic, request.Auth.Type);
    }

    [Fact]
    public void Import_CookieFlag_AddsCookieHeaderRow()
    {
        var request = _importer.Import("curl -b 'session=abc123' https://api.example.test");

        var header = Assert.Single(request.Headers);
        Assert.Equal("Cookie", header.Key);
        Assert.Equal("session=abc123", header.Value);
    }

    [Fact]
    public void Import_IgnoredCommonFlags_DoNotFailTheImport()
    {
        var request = _importer.Import("curl -L -s -k -i -v https://api.example.test");

        Assert.Equal("https://api.example.test", request.Url);
    }

    [Theory]
    [InlineData("it's a value")]
    [InlineData("spaces and \"double quotes\"")]
    public void Import_SingleQuotedValueWithEmbeddedCharacters_IsParsedLiterally(string _)
    {
        // Single quotes are fully literal - no escape processing inside, mirroring
        // CurlGenerator's own ShellQuote. An embedded ' can't appear inside a '...' token at all
        // (that's how POSIX shells work too), so this only exercises spaces/double-quotes here.
        var request = _importer.Import("curl -H 'X-Note: spaces and \"double quotes\"' https://api.example.test");

        Assert.Equal("spaces and \"double quotes\"", request.Headers[0].Value);
    }

    [Fact]
    public void Import_ShellEscapedSingleQuote_RoundTripsFromCurlGeneratorOutput()
    {
        // This is exactly what CurlGenerator.ShellQuote produces for a value containing a '.
        var request = _importer.Import(@"curl -H 'X-Note: it'\''s a test' https://api.example.test");

        Assert.Equal("it's a test", request.Headers[0].Value);
    }

    [Fact]
    public void Import_DoubleQuotedValueWithEscapedQuote_IsUnescaped()
    {
        var request = _importer.Import("curl -H \"X-Note: say \\\"hi\\\"\" https://api.example.test");

        Assert.Equal("say \"hi\"", request.Headers[0].Value);
    }

    [Fact]
    public void Import_UnterminatedSingleQuote_ThrowsCurlImportException()
    {
        var ex = Assert.Throws<CurlImportException>(() => _importer.Import("curl -H 'unterminated https://api.example.test"));
        Assert.Contains("Unterminated", ex.Message);
    }

    [Fact]
    public void Import_UnterminatedDoubleQuote_ThrowsCurlImportException()
    {
        Assert.Throws<CurlImportException>(() => _importer.Import("curl -H \"unterminated https://api.example.test"));
    }

    [Fact]
    public void Import_NotACurlCommand_ThrowsCurlImportException()
    {
        var ex = Assert.Throws<CurlImportException>(() => _importer.Import("wget https://api.example.test"));
        Assert.Contains("curl", ex.Message);
    }

    [Fact]
    public void Import_UnsupportedFlag_ThrowsNamingTheFlagRatherThanSilentlyIgnoringIt()
    {
        var ex = Assert.Throws<CurlImportException>(() => _importer.Import("curl --compressed https://api.example.test"));
        Assert.Contains("--compressed", ex.Message);
    }

    [Fact]
    public void Import_MissingUrl_ThrowsCurlImportException()
    {
        Assert.Throws<CurlImportException>(() => _importer.Import("curl -X GET"));
    }

    [Fact]
    public void Import_MultipleBareUrls_ThrowsCurlImportException()
    {
        Assert.Throws<CurlImportException>(() => _importer.Import("curl https://a.test https://b.test"));
    }

    [Fact]
    public void Import_FlagMissingItsValue_ThrowsCurlImportException()
    {
        Assert.Throws<CurlImportException>(() => _importer.Import("curl -H"));
    }

    [Fact]
    public void Import_EmptyOrWhitespaceCommand_Throws()
    {
        Assert.ThrowsAny<System.Exception>(() => _importer.Import(""));
        Assert.ThrowsAny<System.Exception>(() => _importer.Import("   "));
    }

    [Fact]
    public void RoundTrip_GeneratedCommand_ImportsBackToEquivalentRequest()
    {
        var generator = new CurlGenerator();
        var original = new RequestItem
        {
            Method = HttpMethod.Post,
            Url = "https://api.example.test/pets",
            Headers = { new KeyValueItem { Key = "Accept", Value = "application/json" } },
            BodyMode = RequestBodyMode.Raw,
            Body = "{\"name\":\"Mochi\"}",
            Auth = new RequestAuth { Type = AuthType.Bearer, Token = "abc123" }
        };
        var command = generator.Generate(original, new Dictionary<string, string>(), revealSecrets: true);

        var imported = _importer.Import(command);

        Assert.Equal(original.Method, imported.Method);
        Assert.Equal(original.Url, imported.Url);
        Assert.Equal(original.BodyMode, imported.BodyMode);
        Assert.Equal(original.Body, imported.Body);
        Assert.Equal(AuthType.Bearer, imported.Auth.Type);
        Assert.Equal("abc123", imported.Auth.Token.Value);
        Assert.Contains(imported.Headers, h => h.Key == "Accept" && h.Value == "application/json");
    }

    [Fact]
    public void RoundTrip_UrlEncodedBody_ImportsBackToSameFormData()
    {
        var generator = new CurlGenerator();
        var original = new RequestItem
        {
            Method = HttpMethod.Post,
            Url = "https://api.example.test/token",
            BodyMode = RequestBodyMode.UrlEncoded,
            FormData = { new KeyValueItem { Key = "grant_type", Value = "client_credentials" } }
        };
        var command = generator.Generate(original, new Dictionary<string, string>(), revealSecrets: true);

        var imported = _importer.Import(command);

        Assert.Equal(RequestBodyMode.UrlEncoded, imported.BodyMode);
        Assert.Equal("client_credentials", imported.FormData[0].Value);
    }
}
