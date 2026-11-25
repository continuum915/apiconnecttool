using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.Services;

/// <summary>Collection environments on disk: round trip, migration from the previous schema,
/// selection validation, ID uniqueness, and undecryptable secrets.</summary>
public sealed class CollectionEnvironmentPersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "apiconnecttool-collection-env-" + Guid.NewGuid().ToString("N"));
    private string MainPath => Path.Combine(_directory, "workspace.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private WorkspaceService ServiceWithFile(string json, ISecretStore? store = null)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(MainPath, json);
        return store is null ? new WorkspaceService(_directory) : new WorkspaceService(store, _directory);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsCollectionEnvironmentsActiveIdAndProtectedSecrets()
    {
        var service = new WorkspaceService(_directory);
        var dev = new RequestEnvironment { Name = "dev", Variables = { new() { Key = "baseUrl", InitialValue = "https://dev.example.test" } } };
        var prod = new RequestEnvironment { Name = "prod", IsProduction = true,
            Variables = { new() { Key = "apiKey", IsSecret = true, InitialValue = "prod-initial-secret", CurrentValue = "prod-current-secret", HasCurrentValue = true } } };
        var orders = new RequestCollection { Name = "Orders", Environments = { dev, prod }, ActiveEnvironmentId = prod.Id };
        var billing = new RequestCollection { Name = "Billing" };
        service.SaveWorkspace(new Workspace { Collections = { orders, billing } });

        var loaded = service.LoadWorkspace();

        Assert.Null(service.LastLoadError);
        var loadedOrders = loaded.Collections.Single(collection => collection.Name == "Orders");
        Assert.Equal(new[] { dev.Id, prod.Id }, loadedOrders.Environments.Select(environment => environment.Id));
        Assert.Equal(prod.Id, loadedOrders.ActiveEnvironmentId);
        var loadedProd = loadedOrders.Environments[1];
        Assert.True(loadedProd.IsProduction);
        Assert.Equal("prod-initial-secret", loadedProd.Variables[0].InitialValue.Value);
        Assert.Equal("prod-current-secret", loadedProd.Variables[0].CurrentValue.Value);
        Assert.Equal("https://dev.example.test", loadedOrders.Environments[0].Variables[0].InitialValue.Value);
        var loadedBilling = loaded.Collections.Single(collection => collection.Name == "Billing");
        Assert.Empty(loadedBilling.Environments);
        Assert.Null(loadedBilling.ActiveEnvironmentId);

        var raw = File.ReadAllText(MainPath);
        Assert.DoesNotContain("prod-initial-secret", raw);
        Assert.DoesNotContain("prod-current-secret", raw);
    }

    [Fact]
    public void Save_WritesTheNewSchemaVersion_SoOlderBuildsRefuseTheFile()
    {
        new WorkspaceService(_directory).SaveWorkspace(new Workspace { Collections = { new RequestCollection { Name = "Orders" } } });

        using var document = JsonDocument.Parse(File.ReadAllText(MainPath));

        Assert.Equal(11, Workspace.CurrentSchemaVersion);
        Assert.Equal(Workspace.CurrentSchemaVersion, document.RootElement.GetProperty("SchemaVersion").GetInt32());
        // A build whose CurrentSchemaVersion is 10 sees 11 as a future schema and refuses it;
        // FutureWorkspaceSchemaTests covers that refusal path.
        Assert.True(document.RootElement.GetProperty("SchemaVersion").GetInt32() > 10);
    }

    [Fact]
    public void Load_PreviousSchema_AddsNoEnvironmentsAndResolvesExactlyAsBefore()
    {
        var service = ServiceWithFile("""
            {
              "SchemaVersion": 10,
              "Collections": [
                { "Name": "Orders", "Variables": [ { "Key": "baseUrl", "InitialValue": "https://collection.example.test" }, { "Key": "tenant", "InitialValue": "acme" } ] }
              ],
              "Environments": [
                { "Id": "11111111-1111-1111-1111-111111111111", "Name": "qa", "Variables": [ { "Key": "baseUrl", "InitialValue": "https://qa.example.test" } ] }
              ],
              "SelectedEnvironmentId": "11111111-1111-1111-1111-111111111111",
              "GlobalVariables": [ { "Key": "tenant", "InitialValue": "global" }, { "Key": "region", "InitialValue": "eu" } ]
            }
            """);

        var workspace = service.LoadWorkspace();

        Assert.Null(service.LastLoadError);
        Assert.Equal(Workspace.CurrentSchemaVersion, workspace.SchemaVersion);
        var orders = Assert.Single(workspace.Collections);
        Assert.Empty(orders.Environments);
        Assert.Null(orders.ActiveEnvironmentId);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), workspace.SelectedEnvironmentId);
        var global = workspace.Environments.Single();
        var values = VariableScopeBuilder.Build(workspace, orders, global).Variables;
        Assert.Equal("https://qa.example.test", values["baseUrl"]);
        Assert.Equal("acme", values["tenant"]);
        Assert.Equal("eu", values["region"]);

        using var document = JsonDocument.Parse(File.ReadAllText(MainPath));
        Assert.Equal(Workspace.CurrentSchemaVersion, document.RootElement.GetProperty("SchemaVersion").GetInt32());
    }

    [Fact]
    public void Load_ActiveIdOfAMissingEnvironment_IsClearedReportedAndSaved()
    {
        var service = ServiceWithFile("""
            {
              "SchemaVersion": 11,
              "Collections": [
                { "Name": "Orders", "ActiveEnvironmentId": "22222222-2222-2222-2222-222222222222",
                  "Environments": [ { "Id": "33333333-3333-3333-3333-333333333333", "Name": "dev" } ] },
                { "Name": "Billing", "ActiveEnvironmentId": "44444444-4444-4444-4444-444444444444",
                  "Environments": [ { "Id": "44444444-4444-4444-4444-444444444444", "Name": "dev" } ] }
              ]
            }
            """);

        var workspace = service.LoadWorkspace();

        var orders = workspace.Collections.Single(collection => collection.Name == "Orders");
        Assert.Null(orders.ActiveEnvironmentId);
        Assert.Single(orders.Environments);
        Assert.Equal(Guid.Parse("44444444-4444-4444-4444-444444444444"), workspace.Collections.Single(c => c.Name == "Billing").ActiveEnvironmentId);
        Assert.NotNull(service.LastLoadError);
        Assert.Contains("\"Orders\"", service.LastLoadError);
        Assert.DoesNotContain("\"Billing\"", service.LastLoadError);
        Assert.DoesNotContain("22222222", File.ReadAllText(MainPath));

        service.LoadWorkspace();
        Assert.Null(service.LastLoadError);
    }

    [Fact]
    public void Load_ActiveIdOfAGlobalEnvironment_IsNotAcceptedAsTheCollectionsOwn()
    {
        var service = ServiceWithFile("""
            {
              "SchemaVersion": 11,
              "Environments": [ { "Id": "55555555-5555-5555-5555-555555555555", "Name": "qa" } ],
              "SelectedEnvironmentId": "55555555-5555-5555-5555-555555555555",
              "Collections": [ { "Name": "Orders", "ActiveEnvironmentId": "55555555-5555-5555-5555-555555555555" } ]
            }
            """);

        var workspace = service.LoadWorkspace();

        Assert.Null(workspace.Collections[0].ActiveEnvironmentId);
        Assert.Equal(Guid.Parse("55555555-5555-5555-5555-555555555555"), workspace.SelectedEnvironmentId);
        Assert.Contains("\"Orders\"", service.LastLoadError);
    }

    [Fact]
    public void Load_CollidingEnvironmentIds_AreMadeUniqueAndTheActiveSelectionFollows()
    {
        const string shared = "66666666-6666-6666-6666-666666666666";
        var service = ServiceWithFile($$"""
            {
              "SchemaVersion": 11,
              "Environments": [ { "Id": "{{shared}}", "Name": "global qa" } ],
              "SelectedEnvironmentId": "{{shared}}",
              "Collections": [
                { "Name": "Orders", "ActiveEnvironmentId": "{{shared}}", "Environments": [ { "Id": "{{shared}}", "Name": "qa" } ] },
                { "Name": "Billing", "Environments": [ { "Id": "{{shared}}", "Name": "qa" }, { "Name": "no id" } ] }
              ]
            }
            """);

        var workspace = service.LoadWorkspace();

        var ids = workspace.Environments.Concat(workspace.Collections.SelectMany(collection => collection.Environments))
            .Select(environment => environment.Id).ToList();
        Assert.Equal(4, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, ids);
        Assert.Equal(Guid.Parse(shared), workspace.Environments[0].Id);
        Assert.Equal(Guid.Parse(shared), workspace.SelectedEnvironmentId);
        var orders = workspace.Collections.Single(collection => collection.Name == "Orders");
        Assert.Equal(orders.Environments[0].Id, orders.ActiveEnvironmentId);
        Assert.Null(service.LastLoadError);
    }

    [Fact]
    public void DeletingACollection_RemovesItsEnvironmentsFromDisk()
    {
        var service = new WorkspaceService(_directory);
        var environment = new RequestEnvironment { Name = "dev", Variables = { new() { Key = "marker", InitialValue = "orders-dev-marker" } } };
        var workspace = new Workspace { Collections = { new RequestCollection { Name = "Orders", Environments = { environment }, ActiveEnvironmentId = environment.Id } } };
        service.SaveWorkspace(workspace);

        workspace.Collections.Clear();
        service.SaveWorkspace(workspace);

        var raw = File.ReadAllText(MainPath);
        Assert.DoesNotContain("orders-dev-marker", raw);
        Assert.DoesNotContain(environment.Id.ToString(), raw);
    }

    [Fact]
    public void UndecryptableCollectionEnvironmentSecrets_ArePreservedAndReportedWithCollectionAndEnvironment()
    {
        var service = ServiceWithFile("""
            {
              "SchemaVersion": 11,
              "Collections": [
                { "Name": "Orders", "Environments": [
                  { "Name": "prod", "Variables": [ { "Key": "apiKey", "IsSecret": true, "InitialValue": "ENCRYPTED:opaque-initial",
                    "CurrentValue": "ENCRYPTED:opaque-current", "HasCurrentValue": true } ] } ] }
              ]
            }
            """, new RefusingSecretStore());

        var workspace = service.LoadWorkspace();
        service.SaveWorkspace(workspace);

        var raw = File.ReadAllText(MainPath);
        Assert.Contains("ENCRYPTED:opaque-initial", raw);
        Assert.Contains("ENCRYPTED:opaque-current", raw);
        Assert.Equal(
            new[]
            {
                new UndecryptableSecretLocation(UndecryptableSecretLocationKind.CollectionEnvironment, "prod", UndecryptableSecretField.InitialValue, "apiKey", "Orders"),
                new UndecryptableSecretLocation(UndecryptableSecretLocationKind.CollectionEnvironment, "prod", UndecryptableSecretField.CurrentValue, "apiKey", "Orders")
            },
            service.UndecryptableSecrets);

        var banner = new UndecryptableSecretsBannerViewModel(service.UndecryptableSecrets).Warning;
        var expected = string.Format(ApiConnectTool.Services.Localization.AppLocalization.Get("Str.Workspace.UndecryptableSecret.CollectionEnvironment"),
            "prod", ApiConnectTool.Services.Localization.AppLocalization.Get("Str.Workspace.UndecryptableSecret.Field.InitialValue"), "apiKey", "Orders");
        Assert.Contains(expected, banner);
        Assert.Contains("Orders", expected);
        Assert.Contains("prod", expected);
        Assert.DoesNotContain("opaque", banner);
    }

    /// <summary>Protects reversibly but refuses every decryption, like a workspace moved to another
    /// Windows account.</summary>
    private sealed class RefusingSecretStore : ISecretStore
    {
        public bool TryProtect(string plaintext, out string? protectedPayload)
        {
            protectedPayload = "protected:" + plaintext;
            return true;
        }

        public bool TryUnprotect(string protectedPayload, out string? plaintext)
        {
            plaintext = null;
            return false;
        }
    }
}
