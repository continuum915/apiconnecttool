using System.Runtime.Versioning;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class LinuxSecretServiceStoreTests
{
    [Fact]
    [SupportedOSPlatform("linux")]
    public void TryProtect_PassesSecretViaStdIn_NotArguments()
    {
        var runner = new FakeProcessRunner((_, _, _) => new ProcessRunResult(0, string.Empty, string.Empty));
        var store = new LinuxSecretServiceStore(runner);

        var result = store.TryProtect("super-secret-value", out var payload);

        Assert.True(result);
        Assert.NotNull(payload);
        Assert.NotEmpty(payload!);
        var call = Assert.Single(runner.Calls);
        Assert.Equal("secret-tool", call.FileName);
        Assert.Equal("super-secret-value", call.StandardInput);
        Assert.DoesNotContain("super-secret-value", call.Arguments);
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void TryProtect_WhenSecretToolFails_FailsClosed()
    {
        var runner = new FakeProcessRunner((_, _, _) => new ProcessRunResult(1, string.Empty, "error"));
        var store = new LinuxSecretServiceStore(runner);

        var result = store.TryProtect("value", out var payload);

        Assert.False(result);
        Assert.Null(payload);
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void TryUnprotect_LooksUpByAccount_ReturnsTrimmedStdOut()
    {
        var runner = new FakeProcessRunner((_, _, _) => new ProcessRunResult(0, "super-secret-value\n", string.Empty));
        var store = new LinuxSecretServiceStore(runner);

        var result = store.TryUnprotect("some-account", out var plaintext);

        Assert.True(result);
        Assert.Equal("super-secret-value", plaintext);
        var call = Assert.Single(runner.Calls);
        Assert.Contains("lookup", call.Arguments);
        Assert.Contains("some-account", call.Arguments);
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void TryUnprotect_WhenNotFound_FailsClosed()
    {
        var runner = new FakeProcessRunner((_, _, _) => new ProcessRunResult(1, string.Empty, "No such secret"));
        var store = new LinuxSecretServiceStore(runner);

        var result = store.TryUnprotect("missing-account", out var plaintext);

        Assert.False(result);
        Assert.Null(plaintext);
    }
}
