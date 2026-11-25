using System;
using System.Linq;
using System.Text.Json;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Tests.ViewModels;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class CollectionEnvironmentExportTests
{
    private readonly CollectionExportService _service = new();
    private readonly RequestEnvironment _dev = new()
    {
        Name = "dev",
        Variables =
        {
            new EnvironmentVariable { Key = "host", InitialValue = "https://dev.orders.test" },
            new EnvironmentVariable { Key = "apiKey", InitialValue = "dev-secret-key", CurrentValue = "dev-secret-override", HasCurrentValue = true, IsSecret = true }
        }
    };
    private readonly RequestEnvironment _prod = new()
    {
        Name = "prod",
        IsProduction = true,
        Variables = { new EnvironmentVariable { Key = "host", InitialValue = "https://orders.test" } }
    };
    private readonly RequestCollection _orders;

    public CollectionEnvironmentExportTests()
    {
        _orders = new RequestCollection { Name = "Orders", Environments = { _dev, _prod }, ActiveEnvironmentId = _prod.Id };
    }

    [Fact]
    public void Export_IncludesEnvironmentsWithSecretsRedactedByDefault()
    {
        var json = _service.Export(_orders, Guid.NewGuid(), includeSecrets: false);

        var environments = JsonDocument.Parse(json).RootElement.GetProperty("Collection").GetProperty("Environments");
        Assert.Equal(2, environments.GetArrayLength());
        Assert.Contains("https://dev.orders.test", json);
        Assert.DoesNotContain("dev-secret-key", json);
        Assert.DoesNotContain("dev-secret-override", json);
    }

    [Fact]
    public void Export_WithSecrets_IncludesSecretValues()
    {
        var json = _service.Export(_orders, Guid.NewGuid(), includeSecrets: true);

        Assert.Contains("dev-secret-key", json);
    }

    [Fact]
    public void Export_NeverWritesTheActiveEnvironment()
    {
        var json = _service.Export(_orders, Guid.NewGuid(), includeSecrets: true);

        Assert.False(JsonDocument.Parse(json).RootElement.GetProperty("Collection").TryGetProperty("ActiveEnvironmentId", out _));
        Assert.Equal(_prod.Id, _orders.ActiveEnvironmentId);
    }

    [Fact]
    public void RoundTrip_KeepsEnvironmentsWithFreshIdsAndNoneActive()
    {
        var imported = _service.Import(_service.Export(_orders, Guid.NewGuid(), includeSecrets: false));

        Assert.Equal(new[] { "dev", "prod" }, imported.Environments.Select(environment => environment.Name));
        Assert.DoesNotContain(imported.Environments, environment => environment.Id == _dev.Id || environment.Id == _prod.Id);
        Assert.Null(imported.ActiveEnvironmentId);
        Assert.True(imported.Environments[1].IsProduction);
        var apiKey = imported.Environments[0].Variables[1];
        Assert.True(apiKey.IsSecret);
        Assert.Equal(string.Empty, apiKey.InitialValue.Value);
        Assert.Equal("https://dev.orders.test", imported.Environments[0].Variables[0].InitialValue.Value);
    }

    [Fact]
    public void RoundTrip_WithSecrets_KeepsSecretValues()
    {
        var imported = _service.Import(_service.Export(_orders, Guid.NewGuid(), includeSecrets: true));

        Assert.Equal("dev-secret-key", imported.Environments[0].Variables[1].InitialValue.Value);
    }

    [Fact]
    public void Import_IgnoresAnActiveEnvironmentInTheFile()
    {
        var json = _service.Export(_orders, Guid.NewGuid(), includeSecrets: false)
            .Replace("\"Environments\":", $"\"ActiveEnvironmentId\": \"{_dev.Id}\", \"Environments\":");

        var imported = _service.Import(json);

        Assert.Null(imported.ActiveEnvironmentId);
        Assert.Equal(2, imported.Environments.Count);
    }

    [Fact]
    public void ImportingTwice_GivesEachCopyItsOwnEnvironmentIds()
    {
        var json = _service.Export(_orders, Guid.NewGuid(), includeSecrets: false);
        var vm = new MainWindowViewModel(new FakeWorkspaceService());

        vm.CollectionsPanel.ImportCollection(json);
        vm.CollectionsPanel.ImportCollection(json);

        var ids = vm.CollectionsPanel.Collections.SelectMany(collection => collection.Environments).Select(environment => environment.Id).ToList();
        Assert.Equal(4, ids.Count);
        Assert.Equal(4, ids.Distinct().Count());
        Assert.All(vm.CollectionsPanel.Collections, collection => Assert.Null(collection.ActiveEnvironmentId));
    }

    [Fact]
    public void PostmanExport_OmitsEnvironmentsAndExplainsWhy()
    {
        var json = new PostmanCollectionConverter().Export(_orders, includeSecrets: true);

        Assert.DoesNotContain("https://dev.orders.test", json);
        Assert.Equal("The collection's 2 environment(s) were not exported, because Postman collections have no place for them. Use Export to keep them.",
            CollectionsViewModel.PostmanExportNote(_orders));
        Assert.Null(CollectionsViewModel.PostmanExportNote(new RequestCollection { Name = "Plain" }));
    }
}
