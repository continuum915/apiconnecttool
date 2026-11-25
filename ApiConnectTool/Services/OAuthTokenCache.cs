using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

// Version 2 protects the entire token and context together. Dictionary keys are opaque auth identities.
public sealed class OAuthTokenCache(ISecretStore secretStore, string filePath) : IOAuthTokenCache
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<OAuthToken?> GetAsync(string cacheKey, CancellationToken cancellationToken = default, OAuthContext? context = null)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return ReadTokens(Load(), cacheKey).LastOrDefault(token => context is null || token.Context == context);
        }
        finally { _lock.Release(); }
    }

    public async Task SetAsync(string cacheKey, OAuthToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (!secretStore.TryProtect(JsonSerializer.Serialize(token), out var payload) || payload is null)
            throw new SecretProtectionException(ApiConnectTool.Services.Localization.DefaultStrings.Get("Str.OAuth.ProtectionFailed"));
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var store = Load();
            var retained = new List<string>();
            var now = DateTimeOffset.UtcNow;
            if (store.Entries.TryGetValue(cacheKey, out var entries))
                foreach (var entry in entries)
                    if (ReadToken(entry) is { } existing && existing.Context != token.Context && !IsDead(existing, now)) retained.Add(entry);
            retained.Add(payload);
            store.Entries[cacheKey] = retained;
            Save(store);
        }
        finally { _lock.Release(); }
    }

    public async Task RemoveAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var store = Load();
            if (store.Entries.Remove(cacheKey)) Save(store);
        }
        finally { _lock.Release(); }
    }

    public async Task RemoveAsync(string cacheKey, OAuthContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var store = Load();
            if (!store.Entries.TryGetValue(cacheKey, out var entries)) return;
            var retained = entries.Where(entry => ReadToken(entry) is { } existing && existing.Context != context).ToList();
            if (retained.Count == entries.Count) return;
            if (retained.Count == 0) store.Entries.Remove(cacheKey);
            else store.Entries[cacheKey] = retained;
            Save(store);
        }
        finally { _lock.Release(); }
    }

    // An expired token with no refresh token can never be used again, so it only grows the file.
    private static bool IsDead(OAuthToken token, DateTimeOffset now) =>
        token.ExpiresAtUtc <= now && string.IsNullOrEmpty(token.RefreshToken);

    private IEnumerable<OAuthToken> ReadTokens(StoredCache store, string key) =>
        store.Entries.TryGetValue(key, out var entries)
            ? entries.Select(ReadToken).OfType<OAuthToken>() : [];

    private OAuthToken? ReadToken(string payload)
    {
        if (!secretStore.TryUnprotect(payload, out var json) || json is null) return null;
        try { return JsonSerializer.Deserialize<OAuthToken>(json); }
        catch (JsonException) { return null; }
    }

    private StoredCache Load()
    {
        if (!File.Exists(filePath)) return new();
        try
        {
            var store = JsonSerializer.Deserialize<StoredCache>(File.ReadAllText(filePath));
            // Contextless v1 credentials cannot be trusted for either access or refresh.
            return store is { Version: 2, Entries: not null } ? store : new();
        }
        catch (JsonException) { return new(); }
    }

    private void Save(StoredCache store)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var tempPath = filePath + ".tmp";
        store.Version = 2;
        File.WriteAllText(tempPath, JsonSerializer.Serialize(store));
        File.Move(tempPath, filePath, overwrite: true);
    }

    private sealed class StoredCache
    {
        public int Version { get; set; }
        public Dictionary<string, List<string>> Entries { get; set; } = new();
        public StoredCache() { }
    }
}
