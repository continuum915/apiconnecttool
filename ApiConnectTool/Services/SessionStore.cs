using System;
using System.IO;
using System.Text.Json;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

/// <summary>
/// Persists the open saved-request tab IDs and active tab ID to <c>session.json</c> beside the
/// workspace file. Kept separate from <c>workspace.json</c> because it changes on every tab
/// switch/open/close/reorder and losing it is harmless: a missing or corrupt file simply starts
/// with no open tabs and no warning.
/// </summary>
public sealed class SessionStore : ISessionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _appDataFolder;
    private readonly string _sessionFilePath;
    private readonly string _temporaryFilePath;

    public SessionStore(string appDataFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataFolder);
        _appDataFolder = appDataFolder;
        _sessionFilePath = Path.Combine(appDataFolder, "session.json");
        _temporaryFilePath = Path.Combine(appDataFolder, "session.json.tmp");
    }

    public SessionState Load()
    {
        if (!File.Exists(_sessionFilePath))
        {
            return SessionState.Empty;
        }

        try
        {
            var json = File.ReadAllText(_sessionFilePath);
            var state = JsonSerializer.Deserialize<SessionState>(json, SerializerOptions);
            if (state?.OpenRequestIds is null)
            {
                return SessionState.Empty;
            }

            return state;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return SessionState.Empty;
        }
    }

    public void Save(SessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        try
        {
            Directory.CreateDirectory(_appDataFolder);
            var json = JsonSerializer.Serialize(state, SerializerOptions);
            File.WriteAllText(_temporaryFilePath, json);
            try
            {
                File.Move(_temporaryFilePath, _sessionFilePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(_temporaryFilePath))
                {
                    File.Delete(_temporaryFilePath);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
