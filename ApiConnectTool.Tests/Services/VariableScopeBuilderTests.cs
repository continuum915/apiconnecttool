using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class VariableScopeBuilderTests
{
    [Theory]
    [InlineData("", false, "default", false)]
    [InlineData("legacy", false, "legacy", true)]
    [InlineData("", true, "", true)]
    [InlineData("override", true, "override", true)]
    public void ProvenanceValueKindAgreesWithTheEffectiveMap(string current, bool hasCurrent, string expected, bool isOverride)
    {
        var map = VariableScopeBuilder.Build(environment: new() { Variables = new() {
            new() { Key = "value", InitialValue = "default", CurrentValue = current, HasCurrentValue = hasCurrent } } });
        var definition = Assert.Single(map.Definitions);
        Assert.True(definition.IsWinner);
        Assert.Equal(isOverride, definition.IsOverride);
        Assert.Equal(expected, definition.Value);
        Assert.Equal(expected, map.Variables["value"]);
    }

    [Fact]
    public void ProvenanceExplainsDuplicatesDisabledCaseAndOverrideKinds()
    {
        var workspace = new Workspace { GlobalVariables = new() {
            new() { Key = "key", InitialValue = "default", IsSecret = true },
            new() { Key = "key", CurrentValue = "legacy" },
            new() { Key = "key", InitialValue = "disabled", IsEnabled = false },
            new() { Key = "Key", InitialValue = "upper" },
            new() { Key = " ", InitialValue = "keyless", IsSecret = true } } };
        var environment = new RequestEnvironment { Name = "Local", Variables = new() {
            new() { Key = "key", InitialValue = "fallback", HasCurrentValue = true },
            new() { Key = "disabledOnly", IsSecret = true, IsEnabled = false } } };
        var map = VariableScopeBuilder.Build(workspace, environment: environment);
        Assert.Equal("", map.Variables["key"]);
        Assert.Equal("upper", map.Variables["Key"]);
        Assert.Equal(4, map.Definitions.Count(x => x.Name == "key"));
        var winner = Assert.Single(map.Definitions, x => x.Name == "key" && x.IsWinner);
        Assert.Equal("Local", winner.ScopeName);
        Assert.True(winner.IsOverride);
        Assert.Contains(map.Definitions, x => x.Name == "key" && x.ScopeName == "Globals" && x.IsOverride && !x.IsWinner);
        Assert.Contains(map.Definitions, x => !x.IsEnabled && !x.IsWinner);
        Assert.Contains("key", map.SecretNames);
        Assert.DoesNotContain("Key", map.SecretNames);
        Assert.DoesNotContain("disabledOnly", map.SecretNames);
        Assert.DoesNotContain(" ", map.SecretNames);
        var run = VariableScopeBuilder.Build(workspace, environment: environment, run: new Dictionary<string, string> { ["key"] = "run" });
        Assert.Equal("run", run.Variables["key"]);
        Assert.Equal("Run", Assert.Single(run.Definitions, x => x.Name == "key" && x.IsWinner).ScopeName);
    }

    [Fact]
    public async Task SingleSendAndRunResolveSameScopesAndRedactSecrets()
    {
        var urls = new List<string>();
        var execution = new RequestExecutionService(new TestHttpClientFactory(new FakeHttpMessageHandler(request => {
            urls.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        })));
        var workspace = new Workspace { GlobalVariables = new() { new() { Key = "host", InitialValue = "https://example.test" } } };
        var collection = new RequestCollection { Variables = new() { new() { Key = "secret", InitialValue = "collection-secret", IsSecret = true } } };
        var request = new RequestItem { Url = "{{host}}/{{secret}}", TestScript = "throw new Error(pm.variables.get('secret'));" };
        collection.Requests.Add(request);
        workspace.Collections.Add(collection);
        var tab = ApiConnectTool.ViewModels.RequestTabViewModel.FromRequest(request, execution);
        tab.WorkspaceProvider = () => workspace;
        tab.CollectionProvider = () => collection;
        Assert.DoesNotContain("collection-secret", tab.BuildCurlCommand(false));
        Assert.Contains("collection-secret", tab.BuildCurlCommand(true));
        await tab.SendCommand.ExecuteAsync(null);
        var report = await new CollectionRunner(new RequestRunner(execution), workspace: workspace).RunAsync(collection);
        Assert.Equal(new[] { "https://example.test/collection-secret", "https://example.test/collection-secret" }, urls);
        Assert.DoesNotContain("collection-secret", Assert.Single(report.Entries).ErrorMessage!);
        Assert.DoesNotContain("collection-secret", tab.ResponseBody);
    }
    [Fact]
    public void ScopesMergeInPrecedenceOrderAndRetainSecretNames()
    {
        var workspace = new Workspace { GlobalVariables = new() {
            new() { Key = "key", InitialValue = "global", IsSecret = true },
            new() { Key = "global", InitialValue = "visible" },
            new() { Key = "disabled", InitialValue = "hidden", IsEnabled = false } } };
        var collection = new RequestCollection { Variables = new() { new() { Key = "key", InitialValue = "collection" } } };
        var environment = new RequestEnvironment { Variables = new() { new() { Key = "key", InitialValue = "environment" } } };
        Assert.Equal("environment", VariableScopeBuilder.Build(workspace, collection, environment).Variables["key"]);
        var merged = VariableScopeBuilder.Build(workspace, collection, environment, new Dictionary<string, string> { ["key"] = "run" });
        Assert.Equal("run", merged.Variables["key"]);
        Assert.Equal("visible", merged.Variables["global"]);
        Assert.False(merged.Variables.ContainsKey("disabled"));
        Assert.Contains("key", merged.SecretNames);
    }
}
