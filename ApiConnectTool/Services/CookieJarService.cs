using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Services;

/// <summary>A shared cookie jar whose complete persisted contents are protected by the secret store.</summary>
public sealed class CookieJarService : ICookieJarService
{
    private readonly string _filePath;
    private readonly ISecretStore _secretStore;
    private bool _preserveOriginal;
    public CookieContainer Container { get; } = new();
    public string? PersistenceWarning { get; private set; }

    public CookieJarService(string appDataFolder, ISecretStore? secretStore = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataFolder);
        _filePath = Path.Combine(appDataFolder, "cookies.json");
        _secretStore = secretStore ?? (OperatingSystem.IsWindows() ? new WindowsDpapiSecretStore() : new UnsupportedSecretStore());
        Load();
    }

    private void Load()
    {
        try
        {
            var json = File.ReadAllText(_filePath);
            using var document = JsonDocument.Parse(json);
            var legacy = document.RootElement.ValueKind == JsonValueKind.Array;
            if (!legacy)
            {
                var envelope = JsonSerializer.Deserialize<ProtectedJar>(json);
                if (envelope is not { Version: 1, Payload: not null } ||
                    !_secretStore.TryUnprotect(envelope.Payload, out var plaintext) || plaintext is null)
                    throw new InvalidDataException();
                json = plaintext;
            }
            var stored = JsonSerializer.Deserialize<List<StoredCookie>>(json) ?? throw new InvalidDataException();
            // Validate the entire jar before publishing any cookies.
            var loaded = new CookieContainer();
            foreach (var cookie in stored) loaded.Add((cookie ?? throw new InvalidDataException()).ToCookie());
            foreach (Cookie cookie in loaded.GetAllCookies()) Container.Add(cookie);
            if (legacy)
            {
                Save();
                // Failed migration must never replace the recoverable legacy file on a later send.
                _preserveOriginal = PersistenceWarning is not null;
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            _preserveOriginal = true;
            Warn();
        }
    }

    public void Save()
    {
        if (_preserveOriginal) { Warn(); return; }
        string? tempPath = null;
        try
        {
            var stored = Container.GetAllCookies().Cast<Cookie>().Where(cookie => !cookie.Expired).Select(StoredCookie.FromCookie).ToList();
            if (!_secretStore.TryProtect(JsonSerializer.Serialize(stored), out var payload) || payload is null)
                throw new SecretProtectionException("Cookie protection failed.");
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            tempPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(new ProtectedJar(1, payload)));
            File.Move(tempPath, _filePath, overwrite: true);
            PersistenceWarning = null;
        }
        catch (Exception ex) when (IsStorageFailure(ex)) { Warn(); }
        finally
        {
            if (tempPath is not null)
                try { File.Delete(tempPath); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { }
        }
    }

    public void Clear()
    {
        foreach (Cookie cookie in Container.GetAllCookies()) cookie.Expired = true;
        Save();
    }

    private void Warn() => PersistenceWarning = DefaultStrings.Get("Str.Cookies.PersistenceWarning");
    private static bool IsStorageFailure(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException
        or JsonException or CookieException or ArgumentException or SecretProtectionException
        or System.Security.Cryptography.CryptographicException;
    private sealed record ProtectedJar(int Version, string Payload);
    private sealed record StoredCookie(string Name, string Value, string Domain, string Path, bool Secure, bool HttpOnly, DateTime Expires)
    {
        public static StoredCookie FromCookie(Cookie cookie) =>
            new(cookie.Name, cookie.Value, cookie.Domain, cookie.Path, cookie.Secure, cookie.HttpOnly, cookie.Expires);
        public Cookie ToCookie() => new(Name, Value, Path, Domain)
        {
            Secure = Secure, HttpOnly = HttpOnly, Expires = Expires
        };
    }
}
