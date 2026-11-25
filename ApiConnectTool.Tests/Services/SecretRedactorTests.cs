using System.Collections.Generic;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class SecretRedactorTests
{
    private readonly SecretRedactor _redactor = new();

    [Fact]
    public void Redact_NoSecrets_ReturnsTextUnchanged()
    {
        var result = _redactor.Redact("Authorization: Bearer abc123", []);

        Assert.Equal("Authorization: Bearer abc123", result);
    }

    [Fact]
    public void Redact_EmptyText_ReturnsEmpty()
    {
        var result = _redactor.Redact("", ["abc123"]);

        Assert.Equal("", result);
    }

    [Fact]
    public void Redact_MultipleOccurrencesOfSameSecret_RedactsAll()
    {
        var result = _redactor.Redact("token=abc123, again token=abc123", ["abc123"]);

        Assert.DoesNotContain("abc123", result);
        Assert.Equal(2, CountOccurrences(result, "••••••••"));
    }

    [Fact]
    public void Redact_MultipleDistinctSecrets_RedactsEach()
    {
        var result = _redactor.Redact("Authorization: Basic dXNlcjpwYXNz\nX-Api-Key: k-999", ["dXNlcjpwYXNz", "k-999"]);

        Assert.DoesNotContain("dXNlcjpwYXNz", result);
        Assert.DoesNotContain("k-999", result);
    }

    [Fact]
    public void Redact_SubstringSecrets_RedactsLongestFirstWithNoLeakedFragment()
    {
        // "supersecrettoken" contains "secrettoken" as a substring - redacting the shorter value
        // first would leave a partial "super" fragment of the longer secret exposed.
        var result = _redactor.Redact("value=supersecrettoken", ["secrettoken", "supersecrettoken"]);

        Assert.DoesNotContain("supersecrettoken", result);
        Assert.DoesNotContain("secrettoken", result);
        Assert.DoesNotContain("super••", result);
        Assert.Equal("value=••••••••", result);
    }

    [Fact]
    public void Redact_NullOrEmptySecretValues_AreSkipped()
    {
        var result = _redactor.Redact("hello world", ["", null!, "   " ]);

        Assert.Equal("hello world", result);
    }

    [Fact]
    public void Redact_DuplicateSecretValues_DoNotDoubleRedact()
    {
        var result = _redactor.Redact("token=abc", ["abc", "abc"]);

        Assert.Equal("token=••••••••", result);
    }

    private static int CountOccurrences(string text, string substring)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(substring, index, System.StringComparison.Ordinal)) != -1)
        {
            count++;
            index += substring.Length;
        }

        return count;
    }
}
