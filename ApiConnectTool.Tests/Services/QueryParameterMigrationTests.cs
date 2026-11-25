using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class QueryParameterMigrationTests
{
    [Fact]
    public void SchemaNineMovesUrlQueryAheadOfExistingRowsAndKeepsTheFragment()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ApiConnectToolQueryMigration-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "workspace.json"),
                """{"SchemaVersion":9,"Collections":[{"Requests":[{"Url":"https://example.test/?raw=%2F&flag#frag","QueryParams":[{"Key":"grid","Value":"two words"},{"Key":"off","Value":"saved","IsEnabled":false}]}]}]}""");
            var service = new WorkspaceService(directory);
            var workspace = service.LoadWorkspace();
            Assert.Equal(Workspace.CurrentSchemaVersion, workspace.SchemaVersion);
            var request = workspace.Collections[0].Requests[0];
            Assert.Equal("https://example.test/#frag", request.Url);
            Assert.Equal(new[] { "raw", "flag", "grid", "off" }, request.QueryParams.Select(row => row.Key));
            Assert.Equal("%2F", request.QueryParams[0].Value);
            Assert.True(request.QueryParams[0].UseRawQueryText);
            Assert.False(request.QueryParams[1].HasQueryEquals);
            Assert.False(request.QueryParams[2].UseRawQueryText);
            Assert.False(request.QueryParams[3].IsEnabled);
            service.SaveWorkspace(workspace);
            var restored = service.LoadWorkspace().Collections[0].Requests[0];
            Assert.Equal("https://example.test/?raw=%2F&flag&grid=two words#frag", RequestTabViewModel.FromRequest(restored).RequestUrl);
            Assert.Equal(4, restored.QueryParams.Count);
        }
        finally { Directory.Delete(directory, true); }
    }
}
