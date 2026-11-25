using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class HistoryStoreTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "ApiConnectToolHistoryTests_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private HistoryStore CreateStore() => new(new FakeSecretStore(), _tempDirectory);

    [Fact]
    public async Task Clear_PersistsAnEmptyHistoryThatSurvivesReload()
    {
        var store = CreateStore();
        store.Add(new HistoryEntry { RequestSnapshot = new RequestItem { Name = "one" } });
        store.Add(new HistoryEntry { RequestSnapshot = new RequestItem { Name = "two" } });
        await store.FlushAsync();
        Assert.Equal(2, CreateStore().GetAll().Count);

        store.Clear();
        Assert.Empty(store.GetAll());
        await store.FlushAsync();

        var reloaded = CreateStore();
        Assert.Empty(reloaded.GetAll());
        Assert.Null(reloaded.PersistenceWarning);
        reloaded.Add(new HistoryEntry { RequestSnapshot = new RequestItem { Name = "after" } });
        Assert.Equal("after", Assert.Single(reloaded.GetAll()).RequestSnapshot.Name);
        await reloaded.FlushAsync();
    }

    [Fact]
    public async Task UnprotectableHistory_PreservesBothFilesAndWarns()
    {
        var seed = CreateStore();
        seed.Add(new HistoryEntry { RequestSnapshot = new RequestItem { Auth = new RequestAuth { Token = "private-token" } } });
        await seed.FlushAsync();
        var path = Path.Combine(_tempDirectory, "history.json");
        var original = File.ReadAllBytes(path);
        var backup = File.ReadAllBytes(path + ".bak");
        var store = new HistoryStore(new UnsupportedSecretStore(), _tempDirectory);
        Assert.Empty(store.GetAll());
        Assert.NotNull(store.PersistenceWarning);
        store.Add(new HistoryEntry());
        await store.FlushAsync();
        Assert.NotNull(store.PersistenceWarning);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(backup, File.ReadAllBytes(path + ".bak"));
    }

    [Fact]
    public async Task UnrecoverableFile_RemainsUntouchedAndNewHistoryIsUsableInMemory()
    {
        Directory.CreateDirectory(_tempDirectory);
        var path = Path.Combine(_tempDirectory, "history.json");
        File.WriteAllText(path, "private-corrupt-history");
        var store = CreateStore();
        Assert.Empty(store.GetAll());
        Assert.NotNull(store.PersistenceWarning);
        Assert.DoesNotContain("private", store.PersistenceWarning);
        store.Add(new HistoryEntry { RequestSnapshot = new RequestItem { Name = "New" } });
        await store.FlushAsync();
        Assert.Equal("New", Assert.Single(store.GetAll()).RequestSnapshot.Name);
        Assert.Equal("private-corrupt-history", File.ReadAllText(path));
        Assert.NotNull(store.PersistenceWarning);
    }

    [Fact]
    public async Task RecoveryWarning_IsReportedOnLoadAndNoWriteClearsIt()
    {
        Directory.CreateDirectory(_tempDirectory);
        var path = Path.Combine(_tempDirectory, "history.json");
        File.WriteAllText(path, "private-corrupt-history");
        var store = CreateStore();
        var notifications = 0;
        store.PersistenceWarningChanged += (_, _) => Interlocked.Increment(ref notifications);

        Assert.Empty(store.GetAll());
        Assert.NotNull(store.PersistenceWarning);
        Assert.Equal(1, notifications);
        store.Add(new HistoryEntry());
        store.Add(new HistoryEntry());
        await store.FlushAsync();

        Assert.NotNull(store.PersistenceWarning);
        Assert.Equal(1, notifications);
        Assert.Equal("private-corrupt-history", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public async Task FailedSave_KeepsHistoryInMemoryWarnsAndLaterSaveRetries()
    {
        Directory.CreateDirectory(_tempDirectory);
        var obstacle = Path.Combine(_tempDirectory, "history.json.tmp");
        Directory.CreateDirectory(obstacle);
        var store = CreateStore();
        var notifications = 0;
        store.PersistenceWarningChanged += (_, _) => Interlocked.Increment(ref notifications);
        store.Add(new HistoryEntry { RequestSnapshot = new RequestItem { Name = "First" } });
        await store.FlushAsync();
        Assert.Equal("First", Assert.Single(store.GetAll()).RequestSnapshot.Name);
        Assert.NotNull(store.PersistenceWarning);
        Assert.Equal(1, notifications);
        Directory.Delete(obstacle);
        store.Add(new HistoryEntry { RequestSnapshot = new RequestItem { Name = "Retry" } });
        await store.FlushAsync();
        Assert.Null(store.PersistenceWarning);
        Assert.Equal(2, notifications);
        Assert.Equal(new[] { "Retry", "First" }, CreateStore().GetAll().Select(entry => entry.RequestSnapshot.Name));
    }

    [Fact]
    public async Task Add_ReturnsBeforeDiskWriteAndFlushCompletesOnceWritten()
    {
        var secretStore = new GatedSecretStore("gated-token");
        var store = new HistoryStore(secretStore, _tempDirectory);
        var path = Path.Combine(_tempDirectory, "history.json");

        var add = Task.Run(() => store.Add(new HistoryEntry { RequestSnapshot = new RequestItem { Name = "Queued", Auth = new RequestAuth { Token = "gated-token" } } }));

        await add.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(secretStore.Entered.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal("Queued", Assert.Single(store.GetAll()).RequestSnapshot.Name);
        var flush = store.FlushAsync();
        Assert.False(flush.IsCompleted);
        Assert.False(File.Exists(path));

        secretStore.Release();
        await flush.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("Queued", Assert.Single(CreateStore().GetAll()).RequestSnapshot.Name);
        Assert.True(File.Exists(path + ".bak"));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task AddsDuringAWrite_AreMergedIntoOneFollowingWrite()
    {
        var secretStore = new GatedSecretStore("gated-token");
        var store = new HistoryStore(secretStore, _tempDirectory);
        store.Add(new HistoryEntry { RequestSnapshot = new RequestItem { Name = "A", Auth = new RequestAuth { Token = "gated-token" } } });
        Assert.True(secretStore.Entered.Wait(TimeSpan.FromSeconds(5)));

        store.Add(new HistoryEntry { RequestSnapshot = new RequestItem { Name = "B" } });
        store.Add(new HistoryEntry { RequestSnapshot = new RequestItem { Name = "C" } });
        secretStore.Release();
        await store.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));

        // One write for A, then one merged write of C, B, A: each write protects A's token once.
        Assert.Equal(2, secretStore.GatedCalls);
        Assert.Equal(new[] { "C", "B", "A" }, CreateStore().GetAll().Select(entry => entry.RequestSnapshot.Name));
    }

    [Fact]
    public async Task LegacyFileWithoutRequestBodyTruncatedFlag_LoadsWithFlagFalse()
    {
        var seed = CreateStore();
        seed.Add(new HistoryEntry { RequestSnapshot = new RequestItem { Name = "Legacy", Body = "{\"a\":1}" } });
        await seed.FlushAsync();
        var path = Path.Combine(_tempDirectory, "history.json");
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        foreach (var entry in json)
        {
            var obj = entry!.AsObject();
            foreach (var key in obj.Select(property => property.Key).Where(key => key.Contains("RequestBodyWasTruncated", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                obj.Remove(key);
            }
        }
        File.WriteAllText(path, json.ToJsonString());
        Assert.DoesNotContain("RequestBodyWasTruncated", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);

        var store = CreateStore();

        var loaded = Assert.Single(store.GetAll());
        Assert.Null(store.PersistenceWarning);
        Assert.Equal("Legacy", loaded.RequestSnapshot.Name);
        Assert.Equal("{\"a\":1}", loaded.RequestSnapshot.Body);
        Assert.False(loaded.RequestBodyWasTruncatedForHistory);
    }

    [Fact]
    public async Task RequestBodyTruncatedFlag_RoundTripsThroughDisk()
    {
        var seed = CreateStore();
        seed.Add(new HistoryEntry { RequestSnapshot = new RequestItem { Name = "Big" }, RequestBodyWasTruncatedForHistory = true });
        await seed.FlushAsync();

        Assert.True(Assert.Single(CreateStore().GetAll()).RequestBodyWasTruncatedForHistory);
    }

    [Fact]
    public async Task FlushAsync_WithNothingQueued_Completes()
    {
        var store = CreateStore();

        await store.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(File.Exists(Path.Combine(_tempDirectory, "history.json")));
    }

    [Fact]
    public async Task Add_ThenGetAll_RoundTripsEntryNewestFirst()
    {
        var store = CreateStore();
        var first = new HistoryEntry { RequestSnapshot = new RequestItem { Name = "First" }, SentAtUtc = DateTimeOffset.UtcNow };
        var second = new HistoryEntry { RequestSnapshot = new RequestItem { Name = "Second" }, SentAtUtc = DateTimeOffset.UtcNow };

        store.Add(first);
        store.Add(second);

        var all = store.GetAll();
        Assert.Equal(2, all.Count);
        Assert.Equal("Second", all[0].RequestSnapshot.Name);
        Assert.Equal("First", all[1].RequestSnapshot.Name);
        await store.FlushAsync();
    }

    [Fact]
    public async Task Add_PersistsAcrossNewStoreInstances()
    {
        var store = CreateStore();
        store.Add(new HistoryEntry { RequestSnapshot = new RequestItem { Name = "Persisted" } });
        await store.FlushAsync();

        var reloaded = CreateStore();

        Assert.Single(reloaded.GetAll());
        Assert.Equal("Persisted", reloaded.GetAll()[0].RequestSnapshot.Name);
    }

    [Fact]
    public async Task Add_BeyondMaxEntries_EvictsOldestFirst()
    {
        // Seeds a full-at-cap history file directly (one file write) rather than driving
        // HistoryStore.Add in a tight MaxEntries-sized loop, which is both slow and, on Windows,
        // occasionally flaky (rapid repeated File.Replace calls can transiently fail under AV
        // scanning) - a concern about the test's I/O pattern, not the eviction logic itself.
        Directory.CreateDirectory(_tempDirectory);
        var secretStore = new FakeSecretStore();
        var seedOptions = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new ApiConnectTool.Serialization.EncryptedStringJsonConverter(secretStore), new ApiConnectTool.Serialization.HttpMethodJsonConverter() }
        };
        // Newest first, matching HistoryStore's own on-disk ordering.
        var seedEntries = Enumerable.Range(0, HistoryStore.MaxEntries)
            .Select(i => new HistoryEntry { RequestSnapshot = new RequestItem { Name = $"Entry {HistoryStore.MaxEntries - 1 - i}" } })
            .ToList();
        File.WriteAllText(Path.Combine(_tempDirectory, "history.json"), System.Text.Json.JsonSerializer.Serialize(seedEntries, seedOptions));
        var store = new HistoryStore(secretStore, _tempDirectory);

        store.Add(new HistoryEntry { RequestSnapshot = new RequestItem { Name = "Newest" } });

        var all = store.GetAll();
        Assert.Equal(HistoryStore.MaxEntries, all.Count);
        Assert.Equal("Newest", all[0].RequestSnapshot.Name);
        Assert.DoesNotContain(all, e => e.RequestSnapshot.Name == "Entry 0");
        await store.FlushAsync();
    }

    [Fact]
    public void GetAll_NoFileYet_ReturnsEmpty()
    {
        var store = CreateStore();

        Assert.Empty(store.GetAll());
    }

    [Fact]
    public async Task GetAll_CorruptFileWithGoodBackup_RestoresFromBackup()
    {
        var store = CreateStore();
        store.Add(new HistoryEntry { RequestSnapshot = new RequestItem { Name = "Good" } });
        await store.FlushAsync();
        store.Add(new HistoryEntry { RequestSnapshot = new RequestItem { Name = "Good2" } });
        await store.FlushAsync();

        File.WriteAllText(Path.Combine(_tempDirectory, "history.json"), "{ not valid json");
        var reloaded = CreateStore();

        var all = reloaded.GetAll();
        Assert.NotEmpty(all);
        Assert.All(all, e => Assert.StartsWith("Good", e.RequestSnapshot.Name));
    }

    [Fact]
    public async Task Add_SecretAuthFieldInSnapshot_IsProtectedAtRestNotPlaintext()
    {
        var secretStore = new FakeSecretStore();
        var store = new HistoryStore(secretStore, _tempDirectory);
        var entry = new HistoryEntry
        {
            RequestSnapshot = new RequestItem
            {
                Name = "Auth'd",
                Auth = new RequestAuth { Type = AuthType.Bearer, Token = "literal-secret-value" }
            }
        };

        store.Add(entry);
        await store.FlushAsync();

        var rawJson = File.ReadAllText(Path.Combine(_tempDirectory, "history.json"));
        Assert.DoesNotContain("literal-secret-value", rawJson);
        Assert.Contains("ENCRYPTED:", rawJson);

        var reloaded = store.GetAll();
        Assert.Equal("literal-secret-value", reloaded[0].RequestSnapshot.Auth.Token.Value);
    }

    /// <summary>Protects values like <see cref="FakeSecretStore"/>, but holds the history writer
    /// inside its write whenever it protects <c>gatedPlaintext</c>, until released.</summary>
    internal sealed class GatedSecretStore(string gatedPlaintext) : ISecretStore
    {
        private readonly ManualResetEventSlim _gate = new(false);
        private int _gatedCalls;
        public ManualResetEventSlim Entered { get; } = new(false);
        public int GatedCalls => Volatile.Read(ref _gatedCalls);
        public void Release() => _gate.Set();

        public bool TryProtect(string plaintext, out string protectedValue)
        {
            if (plaintext == gatedPlaintext)
            {
                Interlocked.Increment(ref _gatedCalls);
                Entered.Set();
                _gate.Wait(TimeSpan.FromSeconds(30));
            }
            protectedValue = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plaintext));
            return true;
        }

        public bool TryUnprotect(string protectedValue, out string plaintext)
        {
            plaintext = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue));
            return true;
        }
    }

    private sealed class FakeSecretStore : ISecretStore
    {
        public bool TryProtect(string plaintext, out string protectedValue)
        {
            protectedValue = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plaintext));
            return true;
        }

        public bool TryUnprotect(string protectedValue, out string plaintext)
        {
            try
            {
                plaintext = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue));
                return true;
            }
            catch (FormatException)
            {
                plaintext = string.Empty;
                return false;
            }
        }
    }
}
