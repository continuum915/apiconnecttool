using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class SecretStoreTests
{
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void WindowsDpapiSecretStore_RoundTripsProtectedValue()
    {
        var store = new WindowsDpapiSecretStore();

        Assert.True(store.TryProtect("super-secret-value", out var protectedPayload));
        Assert.NotNull(protectedPayload);
        Assert.True(store.TryUnprotect(protectedPayload!, out var plaintext));
        Assert.Equal("super-secret-value", plaintext);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void WindowsDpapiSecretStore_TryUnprotect_CorruptPayload_FailsClosed()
    {
        var store = new WindowsDpapiSecretStore();

        var result = store.TryUnprotect("not-valid-base64!!!", out var plaintext);

        Assert.False(result);
        Assert.Null(plaintext);
    }

    [Fact]
    public void UnsupportedSecretStore_NeverSucceeds()
    {
        var store = new UnsupportedSecretStore();

        Assert.False(store.TryProtect("value", out var protectedPayload));
        Assert.Null(protectedPayload);
        Assert.False(store.TryUnprotect("ignored", out var plaintext));
        Assert.Null(plaintext);
    }
}
