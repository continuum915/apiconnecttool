using System;
using System.IO;
using ApiConnectTool.Services;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.ViewModels;

public sealed class FutureWorkspaceViewModelTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "apiconnecttool-future-vm-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void FutureWorkspace_ShowsWarningAndBlocksWorkspaceAndSessionWrites()
    {
        Directory.CreateDirectory(_directory);
        var future = """{"SchemaVersion":999,"Collections":[{"Name":"future"}]}""";
        File.WriteAllText(Path.Combine(_directory, "workspace.json"), future);
        File.WriteAllText(Path.Combine(_directory, "workspace.json.bak"), future);
        var before = File.ReadAllBytes(Path.Combine(_directory, "workspace.json"));

        var vm = new MainWindowViewModel(new WorkspaceService(_directory), sessionStore: new SessionStore(_directory));
        vm.CollectionsPanel.AddCollectionCommand.Execute(null);
        vm.AddTabCommand.Execute(null);

        Assert.NotNull(vm.UnsupportedWorkspaceWarning);
        Assert.Contains("999", vm.UnsupportedWorkspaceWarning);
        Assert.Null(vm.WorkspaceWarning);
        Assert.Null(vm.WorkspaceSaveError);
        Assert.False(vm.SaveWorkspaceToDisk());
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(_directory, "workspace.json")));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(_directory, "workspace.json.bak")));
        Assert.False(File.Exists(Path.Combine(_directory, "session.json")));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void SupportedWorkspace_HasNoUnsupportedWarning()
    {
        var vm = new MainWindowViewModel(new WorkspaceService(_directory), sessionStore: new SessionStore(_directory));
        Assert.Null(vm.UnsupportedWorkspaceWarning);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
