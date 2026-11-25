using System.Collections.ObjectModel;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public sealed class AssertionPersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ApiConnectToolAssertions_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    [Fact]
    public void CurrentSchema_RoundTripsOrderedAssertionsWithStableIds()
    {
        var service = new WorkspaceService(_directory);
        var request = new RequestItem { Assertions = new() {
            new() { ExpectedValue = "204", Operator = AssertionOperator.NotEquals, Target = "unused", Description = "first" },
            new() { IsEnabled = false, ExpectedValue = "201", Description = "second" }
        } };
        service.SaveWorkspace(new() { Collections = new() { new() { Requests = new() { request } } } });
        var loaded = new WorkspaceService(_directory).LoadWorkspace();
        Assert.Equal(Workspace.CurrentSchemaVersion, loaded.SchemaVersion);
        var assertions = loaded.Collections[0].Requests[0].Assertions;
        Assert.Equal(request.Assertions.Select(a => a.Id), assertions.Select(a => a.Id));
        Assert.Equal("204", assertions[0].ExpectedValue);
        Assert.Equal(AssertionOperator.NotEquals, assertions[0].Operator);
        Assert.Equal(AssertionKind.StatusCode, assertions[0].Kind);
        Assert.Equal("unused", assertions[0].Target);
        Assert.Equal("first", assertions[0].Description);
        Assert.False(assertions[1].IsEnabled);
    }

    [Theory]
    [InlineData("")]
    [InlineData(", \"Assertions\": null")]
    public void SchemaThree_UpgradesToEmptyNonNullAssertions(string field)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "workspace.json"),
            "{\"SchemaVersion\":3,\"Collections\":[{\"Requests\":[{\"Name\":\"old\"" + field + "}]}]}");
        var service = new WorkspaceService(_directory);
        var loaded = service.LoadWorkspace();
        Assert.Null(service.LastLoadError);
        Assert.Equal(Workspace.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Empty(loaded.Collections[0].Requests[0].Assertions);
        Assert.Empty(service.LoadWorkspace().Collections[0].Requests[0].Assertions);
    }

    [Fact]
    public void NativeExport_RoundTripsAssertionsAndClonePreservesSourceIds()
    {
        var source = new RequestItem { Assertions = new() { new() { ExpectedValue = "204", Description = "created" } } };
        var export = new CollectionExportService();
        var imported = export.Import(export.Export(new() { Requests = new() { source } }, Guid.NewGuid(), false));
        var assertion = Assert.Single(Assert.Single(imported.Requests).Assertions);
        Assert.Equal(source.Assertions[0].Id, assertion.Id);
        Assert.Equal("204", assertion.ExpectedValue);
        Assert.Equal("created", assertion.Description);
        var clone = source.Clone();
        Assert.Equal(source.Assertions[0].Id, clone.Assertions[0].Id);
        clone.Assertions[0].ExpectedValue = "500";
        Assert.Equal("204", source.Assertions[0].ExpectedValue);
    }

    [Fact]
    public void Scripts_RoundTripCurrentSchemaNativeExportAndClone()
    {
        var source = new RequestItem { PreRequestScript = "pm.variables.set('time', 'now');", TestScript = "pm.test('ok', () => pm.expect(1).toEqual(1));" };
        var collection = new RequestCollection { Requests = new() { source } };
        var service = new WorkspaceService(_directory);
        service.SaveWorkspace(new() { Collections = new() { collection } });
        var workspace = service.LoadWorkspace();
        Assert.Equal(Workspace.CurrentSchemaVersion, workspace.SchemaVersion);
        Check(workspace.Collections[0].Requests[0]);
        Check(source.Clone());
        var export = new CollectionExportService();
        Check(Assert.Single(export.Import(export.Export(collection, Guid.NewGuid(), false)).Requests));
        void Check(RequestItem actual)
        {
            Assert.Equal(source.PreRequestScript, actual.PreRequestScript);
            Assert.Equal(source.TestScript, actual.TestScript);
        }
    }
}
