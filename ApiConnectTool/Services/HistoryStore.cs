using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ApiConnectTool.Models;
using ApiConnectTool.Serialization;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Services;

/// <summary>
/// File-backed request history, separate from the workspace file so history doesn't travel with
/// workspace export/backup/share (same reasoning as <see cref="OAuthTokenCache"/>). Bounded via a
/// ring buffer: oldest entries are evicted first once <see cref="MaxEntries"/> is exceeded. Uses
/// the same atomic-write shape as <see cref="WorkspaceService"/> (temp file + File.Replace, a
/// .bak fallback on load failure) and the same EncryptedString protection for any secret-typed
/// auth field embedded in a HistoryEntry's RequestSnapshot. The in-memory list is the source of
/// truth: adding returns at once and a single background writer saves the latest list, so a send
/// never waits on history I/O. Failures surface through <see cref="PersistenceWarning"/>.
/// </summary>
public sealed class HistoryStore : IHistoryStore
{
    public const int MaxEntries = 500;

    private readonly string _appDataFolder;
    private readonly string _historyFilePath;
    private readonly string _backupFilePath;
    private readonly string _temporaryFilePath;
    private readonly JsonSerializerOptions _options;
    private readonly object _lock = new();
    private List<HistoryEntry>? _entries;
    private bool _preserveOriginal;
    private long _version;
    private long _savedVersion;
    private Task? _writer;
    private string? _persistenceWarning;

    public string? PersistenceWarning
    {
        get { lock (_lock) return _persistenceWarning; }
    }

    public event EventHandler? PersistenceWarningChanged;

    public HistoryStore(ISecretStore secretStore, string appDataFolder)
    {
        ArgumentNullException.ThrowIfNull(secretStore);
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataFolder);
        _appDataFolder = appDataFolder;
        _historyFilePath = Path.Combine(appDataFolder, "history.json");
        _backupFilePath = Path.Combine(appDataFolder, "history.json.bak");
        _temporaryFilePath = Path.Combine(appDataFolder, "history.json.tmp");
        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new EncryptedStringJsonConverter(secretStore), new HttpMethodJsonConverter() }
        };
    }

    public void Add(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_lock)
        {
            var entries = _entries ??= Load();
            entries.Insert(0, entry);
            if (entries.Count > MaxEntries)
            {
                entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
            }

            // The original file failed to load: nothing may replace it, and the warning stays.
            if (_preserveOriginal) return;
            _version++;
            _writer ??= Task.Run(WriteQueued);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries = new List<HistoryEntry>();
            // An explicit clear replaces even a history file that failed to load.
            _preserveOriginal = false;
            _version++;
            _writer ??= Task.Run(WriteQueued);
        }
    }

    public IReadOnlyList<HistoryEntry> GetAll()
    {
        lock (_lock)
        {
            return new List<HistoryEntry>(_entries ??= Load());
        }
    }

    public Task FlushAsync()
    {
        lock (_lock)
        {
            return _writer ?? Task.CompletedTask;
        }
    }

    /// <summary>The single background writer: saves the latest list until no add is left unsaved,
    /// so adds that arrive during a write are merged into one following write.</summary>
    private void WriteQueued()
    {
        while (true)
        {
            List<HistoryEntry> snapshot;
            long version;
            lock (_lock)
            {
                if (_savedVersion == _version)
                {
                    _writer = null;
                    return;
                }

                snapshot = new List<HistoryEntry>(_entries!);
                version = _version;
            }

            string? warning = null;
            try
            {
                Save(snapshot);
            }
            catch (Exception)
            {
                // Nothing observes this thread's exceptions, so every failure, expected or not,
                // surfaces as the warning; the next add retries with the full list.
                warning = WarningText;
            }

            lock (_lock) _savedVersion = version;
            try
            {
                SetWarning(warning);
            }
            catch (Exception)
            {
                // A throwing subscriber must not stop this writer; later adds would never be saved.
            }
        }
    }

    private List<HistoryEntry> Load()
    {
        if (TryLoad(_historyFilePath, out var entries, allowMissing: true))
        {
            return entries;
        }

        SetWarning(WarningText);
        _preserveOriginal = true;
        if (TryLoad(_backupFilePath, out entries))
        {
            return entries;
        }

        return new List<HistoryEntry>();
    }

    private static string WarningText => DefaultStrings.Get("Str.History.PersistenceWarning");

    private void SetWarning(string? warning)
    {
        lock (_lock)
        {
            if (_persistenceWarning == warning) return;
            _persistenceWarning = warning;
        }

        PersistenceWarningChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool TryLoad(string path, out List<HistoryEntry> entries, bool allowMissing = false)
    {
        try
        {
            var json = File.ReadAllText(path);
            entries = JsonSerializer.Deserialize<List<HistoryEntry>>(json, _options) ?? throw new JsonException();
            if (entries.Any(entry => entry?.RequestSnapshot?.Auth is not { } auth ||
                auth.Token.FailedToDecrypt || auth.Password.FailedToDecrypt || auth.ClientSecret.FailedToDecrypt))
                throw new JsonException("History contains an unreadable request or protected value.");
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            entries = new List<HistoryEntry>();
            return allowMissing;
        }
        catch (Exception ex) when (PersistenceFailures.IsExpected(ex))
        {
            entries = new List<HistoryEntry>();
            return false;
        }
    }

    private void Save(List<HistoryEntry> entries)
    {
        Directory.CreateDirectory(_appDataFolder);
        var json = JsonSerializer.Serialize(entries, _options);
        File.WriteAllText(_temporaryFilePath, json);

        try
        {
            if (File.Exists(_historyFilePath))
            {
                File.Replace(_temporaryFilePath, _historyFilePath, _backupFilePath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(_temporaryFilePath, _historyFilePath);
                File.Copy(_historyFilePath, _backupFilePath, overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(_temporaryFilePath))
            {
                File.Delete(_temporaryFilePath);
            }
        }
    }
}
