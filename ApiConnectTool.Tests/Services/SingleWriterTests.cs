using System.Diagnostics;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class SingleWriterTests : IDisposable
{
    private readonly List<Process> _children = new();

    [Fact]
    public async Task JunctionAliasCannotAcquireSameDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "ApiConnectTool-writer-" + Guid.NewGuid());
        var folder = Path.Combine(root, "data");
        var alias = Path.Combine(root, "alias");
        Directory.CreateDirectory(folder);
        try
        {
            var linkInfo = new ProcessStartInfo("cmd.exe") { RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var argument in new[] { "/c", "mklink", "/J", alias, folder }) linkInfo.ArgumentList.Add(argument);
            using var link = Process.Start(linkInfo)!;
            await link.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(0, link.ExitCode);
            var owner = Start(folder, "original");
            Assert.Equal("ready", await ReadLine(owner));
            var competitor = Start(alias, "competitor");
            await competitor.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(0, competitor.ExitCode);
            Assert.Contains("already in use", await competitor.StandardOutput.ReadToEndAsync());
            await Stop(owner);
            Assert.Equal("original", Assert.Single(new WorkspaceService(folder).LoadWorkspace().Collections).Name);
        }
        finally
        {
            Dispose();
            if (Directory.Exists(alias)) Directory.Delete(alias);
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LaterLaunchCanOwnDirectoryAfterOwnerExits(bool terminate)
    {
        var folder = Path.Combine(Path.GetTempPath(), "ApiConnectTool-writer-" + Guid.NewGuid());
        try
        {
            var owner = Start(folder, "original");
            Assert.Equal("ready", await ReadLine(owner));
            if (terminate)
            {
                owner.Kill(true);
                await owner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            }
            else await Stop(owner);
            var successor = Start(folder, "successor");
            Assert.Equal("ready", await ReadLine(successor));
            await Stop(successor);
            Assert.Equal(new[] { "original", "successor" }, new WorkspaceService(folder).LoadWorkspace().Collections.Select(c => c.Name));
        }
        finally { Dispose(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task DifferentDirectoriesCanHaveSimultaneousOwners()
    {
        var root = Path.Combine(Path.GetTempPath(), "ApiConnectTool-writer-" + Guid.NewGuid());
        try
        {
            var first = Start(Path.Combine(root, "one"), "one");
            Assert.Equal("ready", await ReadLine(first));
            var second = Start(Path.Combine(root, "two"), "two");
            Assert.Equal("ready", await ReadLine(second));
            await Stop(first);
            await Stop(second);
        }
        finally { Dispose(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task SecondLaunchPreservesCollectionAndEverySiblingFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ApiConnectTool-writer-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            var owner = Start(folder, "first collection");
            Assert.Equal("ready", await ReadLine(owner));
            foreach (var name in new[] { "session.json", "history.json", "oauth-tokens.json", "cookies.json", "workspace.json.bak", "workspace.json.tmp" })
                File.WriteAllText(Path.Combine(folder, name), "preserve " + name);
            var before = Directory.GetFiles(folder).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
            var second = Start(Path.Combine(folder.ToUpperInvariant(), "."), "second collection");
            await second.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(0, second.ExitCode);
            Assert.Contains("already in use", await second.StandardOutput.ReadToEndAsync());
            Assert.Equal(before.Keys.Order(), Directory.GetFiles(folder).Select(Path.GetFileName).Order());
            foreach (var (name, bytes) in before)
                Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(folder, name!)));
            Assert.Equal("first collection", Assert.Single(new WorkspaceService(folder).LoadWorkspace().Collections).Name);
            await Stop(owner);
        }
        finally { Dispose(); Directory.Delete(folder, true); }
    }

    private Process Start(string folder, string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "ApiConnectTool.slnx"))) root = root.Parent!;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var info = new ProcessStartInfo("dotnet") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        info.ArgumentList.Add(Path.Combine(root.FullName, "ApiConnectTool.WriterTestHost", "bin", configuration, "net10.0", "ApiConnectTool.WriterTestHost.dll"));
        info.ArgumentList.Add(folder);
        info.ArgumentList.Add(name);
        var process = Process.Start(info)!;
        _children.Add(process);
        return process;
    }

    private static async Task<string?> ReadLine(Process process) =>
        await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));

    private static async Task Stop(Process process)
    {
        await process.StandardInput.WriteLineAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(0, process.ExitCode);
    }

    public void Dispose()
    {
        foreach (var child in _children)
        {
            if (!child.HasExited) { child.Kill(true); child.WaitForExit(5000); }
            child.Dispose();
        }
        _children.Clear();
    }
}
