#pragma warning disable CA1416
using System.Net;
using System.Text;
using System.Text.Json;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public sealed class ProtectedCookieTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ApiConnectToolProtectedCookies_" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_folder, "cookies.json");
    public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }

    [Fact]
    public void LegacyMigration_ProtectsValuesAndPreservesScopeFlagsAndExpiry()
    {
        Directory.CreateDirectory(_folder);
        var expires = DateTime.UtcNow.AddDays(1);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(new[] {
            new { Name = "session", Value = "private+/session", Domain = "api.example.test", Path = "/private", Secure = true, HttpOnly = true, Expires = expires },
            new { Name = "expired", Value = "old-session", Domain = "api.example.test", Path = "/", Secure = false, HttpOnly = false, Expires = DateTime.UtcNow.AddDays(-1) }
        }));
        var jar = new CookieJarService(_folder);
        Assert.Null(jar.PersistenceWarning);
        var file = File.ReadAllText(FilePath);
        Assert.DoesNotContain("private+/session", file);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes("private+/session")), file);
        Assert.DoesNotContain(Uri.EscapeDataString("private+/session"), file);
        using var json = JsonDocument.Parse(file);
        Assert.Equal(1, json.RootElement.GetProperty("Version").GetInt32());
        var reopened = new CookieJarService(_folder);
        var cookie = Assert.Single(reopened.Container.GetCookies(new Uri("https://api.example.test/private/item")).Cast<Cookie>());
        Assert.Equal("private+/session", cookie.Value);
        Assert.True(cookie.Secure);
        Assert.True(cookie.HttpOnly);
        Assert.Equal(expires, cookie.Expires.ToUniversalTime());
        Assert.Empty(reopened.Container.GetCookies(new Uri("http://api.example.test/private/item")));
        Assert.Empty(reopened.Container.GetCookies(new Uri("https://api.example.test/public")));
        Assert.Empty(reopened.Container.GetCookies(new Uri("https://other.example.test/private")));
        reopened.Clear();
        Assert.Empty(new CookieJarService(_folder).Container.GetAllCookies());
    }

    [Fact]
    public void RefusedLegacyMigration_PreservesFileAndKeepsSessionExplicitlyNonpersistent()
    {
        Directory.CreateDirectory(_folder);
        const string legacy = "[{\"Name\":\"session\",\"Value\":\"recover-me\",\"Domain\":\"api.example.test\",\"Path\":\"/\",\"Secure\":false,\"HttpOnly\":true,\"Expires\":\"0001-01-01T00:00:00\"}]";
        File.WriteAllText(FilePath, legacy);
        var jar = new CookieJarService(_folder, new UnsupportedSecretStore());
        Assert.Single(jar.Container.GetAllCookies());
        Assert.NotNull(jar.PersistenceWarning);
        Assert.DoesNotContain("recover-me", jar.PersistenceWarning);
        jar.Save();
        jar.Clear();
        Assert.Empty(jar.Container.GetCookies(new Uri("https://api.example.test")));
        Assert.Equal(legacy, File.ReadAllText(FilePath));
        Assert.Single(Directory.GetFiles(_folder));
    }

    [Theory]
    [InlineData("broken-secret-json")]
    [InlineData("{\"Version\":99,\"Payload\":\"recover-me\"}")]
    [InlineData("{\"Version\":1,\"Payload\":\"recover-me\"}")]
    [InlineData("[null]")]
    public void UnreadableJar_RemainsUntouchedAndStartsEmpty(string original)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, original);
        var jar = new CookieJarService(_folder, new UnsupportedSecretStore());
        Assert.Empty(jar.Container.GetCookies(new Uri("https://api.example.test")));
        Assert.NotNull(jar.PersistenceWarning);
        jar.Container.Add(new Uri("https://example.test"), new Cookie("new", "new-secret"));
        jar.Save();
        jar.Clear();
        Assert.Equal(original, File.ReadAllText(FilePath));
    }

    [Fact]
    public void InaccessibleStartupPath_ProducesWarningInsteadOfTreatingItAsMissing()
    {
        Directory.CreateDirectory(FilePath);
        var jar = new CookieJarService(_folder);
        Assert.NotNull(jar.PersistenceWarning);
        jar.Container.Add(new Uri("https://example.test"), new Cookie("session", "private-session"));
        jar.Save();
        Assert.Single(jar.Container.GetAllCookies());
        Assert.True(Directory.Exists(FilePath));
    }

    [Fact]
    public void RefusedProtection_DoesNotCreateFile_AndCanRetrySuccessfully()
    {
        var store = new SwitchableStore();
        var jar = new CookieJarService(_folder, store);
        jar.Container.Add(new Uri("https://example.test"), new Cookie("session", "private-session"));
        jar.Save();
        Assert.NotNull(jar.PersistenceWarning);
        Assert.False(File.Exists(FilePath));
        store.Refuse = false;
        jar.Save();
        Assert.Null(jar.PersistenceWarning);
        Assert.Equal("private-session", new CookieJarService(_folder, store).Container.GetCookies(new Uri("https://example.test"))["session"]!.Value);
    }

    private sealed class SwitchableStore : ISecretStore
    {
        public bool Refuse { get; set; } = true;
        private readonly WindowsDpapiSecretStore _store = new();
        public bool TryProtect(string plaintext, out string? protectedPayload)
        {
            protectedPayload = null;
            return !Refuse && _store.TryProtect(plaintext, out protectedPayload);
        }
        public bool TryUnprotect(string payload, out string? plaintext) => _store.TryUnprotect(payload, out plaintext);
    }
}
