using System.Collections.Generic;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class VariableResolverTests
{
    private readonly VariableResolver _resolver = new();

    [Fact]
    public void Resolve_NoVariablesPresent_ReturnsTemplateUnchanged()
    {
        var result = _resolver.Resolve("https://api.example.test/pets", new Dictionary<string, string>());

        Assert.Equal("https://api.example.test/pets", result.Value);
        Assert.Empty(result.UnresolvedVariableNames);
    }

    [Fact]
    public void Resolve_KnownVariable_Substitutes()
    {
        var variables = new Dictionary<string, string> { ["baseUrl"] = "https://api.example.test" };

        var result = _resolver.Resolve("{{baseUrl}}/pets", variables);

        Assert.Equal("https://api.example.test/pets", result.Value);
        Assert.Empty(result.UnresolvedVariableNames);
    }

    [Fact]
    public void Resolve_UnknownVariable_LeavesTokenLiteralAndReportsName()
    {
        var result = _resolver.Resolve("{{missing}}/pets", new Dictionary<string, string>());

        Assert.Equal("{{missing}}/pets", result.Value);
        Assert.Equal(["missing"], result.UnresolvedVariableNames);
    }

    [Fact]
    public void Resolve_PartialResolution_SubstitutesKnownAndReportsUnknown()
    {
        var variables = new Dictionary<string, string> { ["baseUrl"] = "https://api.example.test" };

        var result = _resolver.Resolve("{{baseUrl}}/{{missing}}", variables);

        Assert.Equal("https://api.example.test/{{missing}}", result.Value);
        Assert.Equal(["missing"], result.UnresolvedVariableNames);
    }

    [Fact]
    public void Resolve_DuplicateUnknownVariable_ReportedOnce()
    {
        var result = _resolver.Resolve("{{missing}}/{{missing}}", new Dictionary<string, string>());

        Assert.Equal(["missing"], result.UnresolvedVariableNames);
    }

    [Fact]
    public void Resolve_TrimsWhitespaceInsideTokens()
    {
        var variables = new Dictionary<string, string> { ["token"] = "abc123" };

        var result = _resolver.Resolve("Bearer {{ token }}", variables);

        Assert.Equal("Bearer abc123", result.Value);
    }

    [Fact]
    public void Resolve_NullOrEmptyTemplate_ReturnsEmptyPassthrough()
    {
        Assert.Equal(string.Empty, _resolver.Resolve(null, new Dictionary<string, string>()).Value);
        Assert.Equal(string.Empty, _resolver.Resolve(string.Empty, new Dictionary<string, string>()).Value);
    }

    [Fact]
    public void Resolve_KnownVariable_ReportsItAsResolved()
    {
        var variables = new Dictionary<string, string> { ["baseUrl"] = "https://api.example.test" };

        var result = _resolver.Resolve("{{baseUrl}}/pets", variables);

        Assert.Equal(["baseUrl"], result.ResolvedVariableNames);
    }

    [Fact]
    public void Resolve_DuplicateKnownVariable_ReportedOnceInResolvedNames()
    {
        var variables = new Dictionary<string, string> { ["id"] = "42" };

        var result = _resolver.Resolve("{{id}}/{{id}}", variables);

        Assert.Equal(["id"], result.ResolvedVariableNames);
    }

    [Fact]
    public void Resolve_UnknownVariable_IsNotReportedAsResolved()
    {
        var result = _resolver.Resolve("{{missing}}/pets", new Dictionary<string, string>());

        Assert.Empty(result.ResolvedVariableNames);
    }
}
