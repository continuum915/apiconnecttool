using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class OAuthTokenCacheTests : IDisposable
{
    private readonly string _filePath = Path.Combine(Path.GetTempPath(), $"apiconnecttool-oauth-cache-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task SetThenGet_RoundTripsAccessAndRefreshToken()
    {
        var cache = new OAuthTokenCache(new ReversibleSecretStore(), _filePath);
        var expiry = DateTimeOffset.UtcNow.AddHours(1);
        var token = new OAuthToken("access-1", "refresh-1", expiry);

        await cache.SetAsync("key-1", token);
        var loaded = await cache.GetAsync("key-1");

        Assert.NotNull(loaded);
        Assert.Equal("access-1", loaded!.AccessToken);
        Assert.Equal("refresh-1", loaded.RefreshToken);
        Assert.Equal(expiry, loaded.ExpiresAtUtc);
    }

    [Fact]
    public async Task Get_UnknownKey_ReturnsNull()
    {
        var cache = new OAuthTokenCache(new ReversibleSecretStore(), _filePath);

        var loaded = await cache.GetAsync("missing");

        Assert.Null(loaded);
    }

    [Fact]
    public async Task Remove_DeletesEntry()
    {
        var cache = new OAuthTokenCache(new ReversibleSecretStore(), _filePath);
        await cache.SetAsync("key-1", new OAuthToken("access", null, DateTimeOffset.UtcNow.AddHours(1)));

        await cache.RemoveAsync("key-1");

        Assert.Null(await cache.GetAsync("key-1"));
    }

    [Fact]
    public async Task SetAsync_WhenSecretStoreCannotProtect_ThrowsAndDoesNotPersistPlaintext()
    {
        var cache = new OAuthTokenCache(new FailingSecretStore(), _filePath);

        await Assert.ThrowsAsync<SecretProtectionException>(() =>
            cache.SetAsync("key-1", new OAuthToken("super-secret-access-token", null, DateTimeOffset.UtcNow.AddHours(1))));

        if (File.Exists(_filePath))
        {
            Assert.DoesNotContain("super-secret-access-token", File.ReadAllText(_filePath));
        }
    }

    [Fact]
    public async Task RemoveByContext_RemovesOnlyThatContext()
    {
        var cache = new OAuthTokenCache(new ReversibleSecretStore(), _filePath);
        var dev = Context("https://dev.test/token");
        var prod = Context("https://prod.test/token");
        await cache.SetAsync("key-1", new OAuthToken("dev-access", "dev-refresh", DateTimeOffset.UtcNow.AddHours(1)) { Context = dev });
        await cache.SetAsync("key-1", new OAuthToken("prod-access", "prod-refresh", DateTimeOffset.UtcNow.AddHours(1)) { Context = prod });
        await cache.SetAsync("key-2", new OAuthToken("other-key", null, DateTimeOffset.UtcNow.AddHours(1)) { Context = dev });

        await cache.RemoveAsync("key-1", dev);

        Assert.Null(await cache.GetAsync("key-1", context: dev));
        Assert.Equal("prod-access", (await cache.GetAsync("key-1", context: prod))!.AccessToken);
        Assert.Equal("other-key", (await cache.GetAsync("key-2", context: dev))!.AccessToken);
    }

    [Fact]
    public async Task Set_PrunesOtherContextsThatAreExpiredWithoutRefreshToken()
    {
        var cache = new OAuthTokenCache(new ReversibleSecretStore(), _filePath);
        var dead = Context("https://dead.test/token");
        var refreshable = Context("https://refreshable.test/token");
        var live = Context("https://live.test/token");
        var current = Context("https://current.test/token");
        var past = DateTimeOffset.UtcNow.AddHours(-1);
        await cache.SetAsync("key-1", new OAuthToken("dead", null, past) { Context = dead });
        await cache.SetAsync("key-1", new OAuthToken("refreshable", "refresh", past) { Context = refreshable });
        await cache.SetAsync("key-1", new OAuthToken("live", null, DateTimeOffset.UtcNow.AddHours(1)) { Context = live });

        await cache.SetAsync("key-1", new OAuthToken("current", null, DateTimeOffset.UtcNow.AddHours(1)) { Context = current });

        Assert.Null(await cache.GetAsync("key-1", context: dead));
        Assert.NotNull(await cache.GetAsync("key-1", context: refreshable));
        Assert.NotNull(await cache.GetAsync("key-1", context: live));
        Assert.NotNull(await cache.GetAsync("key-1", context: current));
    }

    private static OAuthContext Context(string tokenUrl) =>
        new(null, OAuthGrantType.ClientCredentials, string.Empty, tokenUrl, "client", "secret", string.Empty, string.Empty);

    public void Dispose()
    {
        if (File.Exists(_filePath)) File.Delete(_filePath);
    }

    private sealed class ReversibleSecretStore : ISecretStore
    {
        public bool TryProtect(string plaintext, out string? protectedPayload)
        {
            protectedPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));
            return true;
        }

        public bool TryUnprotect(string protectedPayload, out string? plaintext)
        {
            plaintext = Encoding.UTF8.GetString(Convert.FromBase64String(protectedPayload));
            return true;
        }
    }

    private sealed class FailingSecretStore : ISecretStore
    {
        public bool TryProtect(string plaintext, out string? protectedPayload)
        {
            protectedPayload = null;
            return false;
        }

        public bool TryUnprotect(string protectedPayload, out string? plaintext)
        {
            plaintext = null;
            return false;
        }
    }
}
