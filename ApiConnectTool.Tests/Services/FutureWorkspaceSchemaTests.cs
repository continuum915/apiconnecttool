using System;
using System.IO;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public sealed class FutureWorkspaceSchemaTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "apiconnecttool-future-" + Guid.NewGuid().ToString("N"));
    private string MainPath => Path.Combine(_directory, "workspace.json");
    private string BackupPath => Path.Combine(_directory, "workspace.json.bak");

    private const string FutureJson = """{"SchemaVersion":999,"FutureOnly":{"keep":"me"},"Collections":[{"Name":"future"}]}""";
    private const string OlderBackupJson = """{"SchemaVersion":5,"Collections":[{"Name":"older"}]}""";

    private WorkspaceService CreateWithFutureFiles()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(MainPath, FutureJson);
        File.WriteAllText(BackupPath, OlderBackupJson);
        return new WorkspaceService(_directory);
    }

    [Fact]
    public void Load_FutureSchema_ReportsUnsupportedVersionAndNotCorruption()
    {
        var service = CreateWithFutureFiles();

        var workspace = service.LoadWorkspace();

        Assert.Equal(999, service.UnsupportedSchemaVersion);
        Assert.Null(service.LastLoadError);
        Assert.Empty(workspace.Collections);
        Assert.Empty(Directory.GetFiles(_directory, "workspace.unreadable-*"));
    }

    [Fact]
    public void Load_FutureSchema_DoesNotRestoreLowerSchemaBackupOrChangeFiles()
    {
        var service = CreateWithFutureFiles();
        var mainBytes = File.ReadAllBytes(MainPath);
        var backupBytes = File.ReadAllBytes(BackupPath);

        var workspace = service.LoadWorkspace();

        Assert.DoesNotContain(workspace.Collections, collection => collection.Name == "older");
        Assert.Equal(mainBytes, File.ReadAllBytes(MainPath));
        Assert.Equal(backupBytes, File.ReadAllBytes(BackupPath));
    }

    [Fact]
    public void Save_FutureSchema_IsRefusedAndLeavesFilesUntouched()
    {
        var service = CreateWithFutureFiles();
        var mainBytes = File.ReadAllBytes(MainPath);
        var backupBytes = File.ReadAllBytes(BackupPath);
        var workspace = service.LoadWorkspace();

        Assert.Throws<UnsupportedWorkspaceVersionException>(() => service.SaveWorkspace(workspace));
        Assert.Throws<UnsupportedWorkspaceVersionException>(() => service.SaveWorkspace(new Workspace()));

        Assert.Equal(mainBytes, File.ReadAllBytes(MainPath));
        Assert.Equal(backupBytes, File.ReadAllBytes(BackupPath));
        Assert.False(File.Exists(Path.Combine(_directory, "workspace.json.tmp")));
    }

    [Fact]
    public void Save_BeforeAnyLoad_StillRefusesFutureSchemaOnDisk()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(MainPath, FutureJson);
        var service = new WorkspaceService(_directory);

        Assert.Throws<UnsupportedWorkspaceVersionException>(() => service.SaveWorkspace(new Workspace()));

        Assert.Equal(FutureJson, File.ReadAllText(MainPath));
    }

    [Fact]
    public void CurrentSchema_StillLoadsAndSaves()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(MainPath, $$"""{"SchemaVersion":{{Workspace.CurrentSchemaVersion}},"Collections":[{"Name":"now"}]}""");
        var service = new WorkspaceService(_directory);

        var workspace = service.LoadWorkspace();
        service.SaveWorkspace(workspace);

        Assert.Null(service.UnsupportedSchemaVersion);
        Assert.Equal("now", workspace.Collections[0].Name);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
