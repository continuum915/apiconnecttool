using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class OpenApiDailyDriverTests
{
    [Fact]
    public void NoServerAndMixedSecurityKeepPerRequestBehavior()
    {
        var result = new OpenApiImporter().ImportWithReport("""
            {"openapi":"3.0.0","security":[{"bearer":[]}],
             "components":{"securitySchemes":{"bearer":{"type":"http","scheme":"bearer"}}},
             "paths":{"/secured":{"get":{}},"/public":{"get":{"security":[]}}}}
            """);
        Assert.Empty(result.Collection.Variables);
        Assert.Equal(AuthType.None, result.Collection.Auth.Type);
        Assert.Equal(AuthType.Bearer, result.Collection.Requests[0].Auth.Type);
        Assert.Equal(AuthType.None, result.Collection.Requests[1].Auth.Type);
        Assert.Equal("/secured", result.Collection.Requests[0].Url);
        Assert.Equal(1, result.Report.MappedRequestCount);
    }
    [Fact]
    public void SharedSecurityAndFirstServerAreCollectionSettings()
    {
        var collection = new OpenApiImporter().Import("""
            {"openapi":"3.0.0","servers":[{"url":"https://example.test/v1/"}],"security":[{"bearer":[]}],
             "components":{"securitySchemes":{"bearer":{"type":"http","scheme":"bearer"}}},
             "paths":{"/one":{"get":{}},"/two":{"post":{}}}}
            """);
        Assert.Equal("https://example.test/v1", Assert.Single(collection.Variables).InitialValue.Value);
        Assert.Equal("baseUrl", collection.Variables[0].Key);
        Assert.Equal(new[] { "{{baseUrl}}/one", "{{baseUrl}}/two" }, collection.Requests.Select(request => request.Url));
        Assert.Equal(AuthType.Bearer, collection.Auth.Type);
        Assert.All(collection.Requests, request => Assert.Equal(AuthType.Inherit, request.Auth.Type));
    }
}
