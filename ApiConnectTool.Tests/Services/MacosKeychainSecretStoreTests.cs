using System.Runtime.Versioning;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class MacosKeychainSecretStoreTests
{
    [Fact]
    [SupportedOSPlatform("macos")]
    public void TryProtect_StoresUnderGeneratedAccount_ReturnsAccountAsPayload()
    {
        var runner = new FakeProcessRunner((_, _, _) => new ProcessRunResult(0, string.Empty, string.Empty));
        var store = new MacosKeychainSecretStore(runner);

        var result = store.TryProtect("super-secret-value", out var payload);

        Assert.True(result);
        Assert.NotNull(payload);
        Assert.NotEmpty(payload!);
        var call = Assert.Single(runner.Calls);
        Assert.Equal("security", call.FileName);
        Assert.Contains("add-generic-password", call.Arguments);
        Assert.Contains("super-secret-value", call.Arguments);
    }

    [Fact]
    [SupportedOSPlatform("macos")]
    public void TryProtect_WhenSecurityToolFails_FailsClosed()
    {
        var runner = new FakeProcessRunner((_, _, _) => new ProcessRunResult(1, string.Empty, "denied"));
        var store = new MacosKeychainSecretStore(runner);

        var result = store.TryProtect("value", out var payload);

        Assert.False(result);
        Assert.Null(payload);
    }

    [Fact]
    [SupportedOSPlatform("macos")]
    public void TryUnprotect_LooksUpByAccount_ReturnsTrimmedStdOut()
    {
        var runner = new FakeProcessRunner((_, _, _) => new ProcessRunResult(0, "super-secret-value\n", string.Empty));
        var store = new MacosKeychainSecretStore(runner);

        var result = store.TryUnprotect("some-account", out var plaintext);

        Assert.True(result);
        Assert.Equal("super-secret-value", plaintext);
        var call = Assert.Single(runner.Calls);
        Assert.Contains("find-generic-password", call.Arguments);
        Assert.Contains("some-account", call.Arguments);
    }

    [Fact]
    [SupportedOSPlatform("macos")]
    public void TryUnprotect_WhenNotFound_FailsClosed()
    {
        var runner = new FakeProcessRunner((_, _, _) => new ProcessRunResult(44, string.Empty, "not found"));
        var store = new MacosKeychainSecretStore(runner);

        var result = store.TryUnprotect("missing-account", out var plaintext);

        Assert.False(result);
        Assert.Null(plaintext);
    }
}
