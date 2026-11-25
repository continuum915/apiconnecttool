using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class PostmanFidelityTests
{
    [Fact]
    public void PostmanAndNativeRoundTripAuthAndVariablesWithDefaultRedaction()
    {
        var converter = new PostmanCollectionConverter();
        var collection = converter.Import(Fixture);
        collection.Requests[0].PreRequestScript = "pm.variables.set('key','value');";
        var postman = converter.Export(collection, includeSecrets: false);
        var nativeService = new CollectionExportService();
        var native = nativeService.Export(collection, Guid.NewGuid(), includeSecrets: false);
        foreach (var json in new[] { postman, native })
        {
            Assert.DoesNotContain("collection-private", json); Assert.DoesNotContain("folder-private", json);
            Assert.DoesNotContain("variable-private", json);
        }
        foreach (var imported in new[] { converter.Import(postman), nativeService.Import(native) })
        {
            Assert.Equal(AuthType.Bearer, imported.Auth.Type);
            Assert.Equal(AuthType.Basic, Assert.Single(imported.Folders).Auth.Type);
            Assert.Equal(AuthType.Inherit, imported.Requests.Single(request => request.Name == "Inherited").Auth.Type);
            Assert.Equal("https://example.test", imported.Variables.Single(variable => variable.Key == "baseUrl").InitialValue.Value);
            Assert.False(imported.Variables.Single(variable => variable.Key == "disabled").IsEnabled);
            Assert.True(imported.Variables.Single(variable => variable.Key == "secret").IsSecret);
        }
        Assert.Contains(converter.ImportWithReport(postman).Report.Warnings, warning => warning.Contains("prerequest") && warning.Contains("Inherited"));
    }
    private static string Fixture => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "daily-driver-postman.json"));
    [Fact]
    public async Task ImportedCollectionResolvesVariablesAndInheritedAuthAndReportsDrops()
    {
        var result = new PostmanCollectionConverter().ImportWithReport(Fixture);
        Assert.Equal(AuthType.Bearer, result.Collection.Auth.Type);
        Assert.Equal(AuthType.Inherit, result.Collection.Requests.Single(request => request.Name == "Inherited").Auth.Type);
        Assert.Equal(AuthType.None, result.Collection.Requests.Single(request => request.Name == "Public").Auth.Type);
        Assert.False(result.Collection.Variables.Single(variable => variable.Key == "disabled").IsEnabled);
        Assert.Contains(result.Report.Warnings, warning => warning.Contains("Inherited") && warning.Contains("test"));
        Assert.Contains(result.Report.Warnings, warning => warning.Contains("Inherited API") && warning.Contains("prerequest"));
        Assert.Contains(result.Report.Warnings, warning => warning.Contains("digest"));
        var sent = new List<string>();
        var runner = RequestRunnerTests.Runner(request => {
            sent.Add(request.RequestUri!.AbsoluteUri + "|" + request.Headers.Authorization);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        });
        await new CollectionRunner(runner).RunAsync(result.Collection);
        Assert.Contains("https://example.test/inherited|Bearer collection-private", sent);
        Assert.Contains("https://example.test/public|", sent);
        Assert.Contains("https://example.test/child|Basic dXNlcjpmb2xkZXItcHJpdmF0ZQ==", sent);
    }
}
