using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using ApiConnectTool.Models;
using ApiConnectTool.Serialization;

namespace ApiConnectTool.Services;

public class WorkspaceService : IWorkspaceService
{
    private readonly string _appDataFolder;
    private readonly string _workspaceFilePath;
    private readonly string _backupFilePath;
    private readonly string _temporaryFilePath;
    private readonly JsonSerializerOptions _options;

    public string? LastLoadError { get; private set; }

    public int? UnsupportedSchemaVersion { get; private set; }

    public IReadOnlyList<UndecryptableSecretLocation> UndecryptableSecrets { get; private set; } = [];

    public WorkspaceService()
        : this(CreatePlatformSecretStore(), DefaultAppDataFolder)
    {
    }

    public WorkspaceService(string appDataFolder)
        : this(CreatePlatformSecretStore(), appDataFolder)
    {
    }

    public WorkspaceService(ISecretStore secretStore)
        : this(secretStore, DefaultAppDataFolder)
    {
    }

    public WorkspaceService(ISecretStore secretStore, string appDataFolder)
    {
        ArgumentNullException.ThrowIfNull(secretStore);
        _appDataFolder = appDataFolder;
        _workspaceFilePath = Path.Combine(appDataFolder, "workspace.json");
        _backupFilePath = Path.Combine(appDataFolder, "workspace.json.bak");
        _temporaryFilePath = Path.Combine(appDataFolder, "workspace.json.tmp");
        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new EncryptedStringJsonConverter(secretStore, preserveUndecryptable: true), new HttpMethodJsonConverter() }
        };
    }

    private static string DefaultAppDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApiConnectTool");

    private static ISecretStore CreatePlatformSecretStore() =>
        OperatingSystem.IsWindows() ? new WindowsDpapiSecretStore() : new UnsupportedSecretStore();

    public Workspace LoadWorkspace()
    {
        LastLoadError = null;
        UnsupportedSchemaVersion = null;
        UndecryptableSecrets = [];
        if (!File.Exists(_workspaceFilePath))
        {
            return new Workspace();
        }

        // Inspected before deserialization, normalization, or backup recovery: a newer schema is
        // valid data this build cannot represent, so it is neither corruption nor grounds to
        // restore an older backup. The files are left exactly as found.
        if (ReadFutureSchemaVersion() is { } futureVersion)
        {
            UnsupportedSchemaVersion = futureVersion;
            return new Workspace();
        }

        var clearedSelections = new List<string>();
        if (TryLoad(_workspaceFilePath, out var workspace, out var mainError))
        {
            if (WorkspaceMigrator.Normalize(workspace, clearedSelections))
            {
                TrySaveDuringLoad(workspace);
            }
            LastLoadError = ClearedSelectionsWarning(clearedSelections);
            UndecryptableSecrets = UndecryptableSecretScanner.Scan(workspace);
            return workspace;
        }

        // Copy the unreadable file aside before anything can overwrite it: the next save
        // rotates it into workspace.json.bak and the one after that replaces the .bak, so
        // without this copy a file that was only partly damaged would be gone for good.
        var preservedPath = PreserveUnreadableWorkspaceFile();
        var preservedNote = preservedPath is null
            ? string.Empty
            : $" A copy of the unreadable file was kept at {preservedPath}.";

        if (File.Exists(_backupFilePath) && TryLoad(_backupFilePath, out workspace, out _))
        {
            LastLoadError = $"The workspace file could not be loaded and the last known-good backup was restored: {mainError}{preservedNote}";
            WorkspaceMigrator.Normalize(workspace, clearedSelections);
            if (ClearedSelectionsWarning(clearedSelections) is { } clearedWarning) LastLoadError += " " + clearedWarning;
            TrySaveDuringLoad(workspace);
            UndecryptableSecrets = UndecryptableSecretScanner.Scan(workspace);
            return workspace;
        }

        LastLoadError = $"The workspace file could not be loaded: {mainError}{preservedNote}";
        return new Workspace();
    }

    private static string? ClearedSelectionsWarning(List<string> collectionNames) => collectionNames.Count == 0
        ? null
        : string.Join(" ", collectionNames.Select(name =>
            string.Format(Localization.DefaultStrings.Get("Str.Workspace.StaleCollectionEnvironment"), name)));

    /// <summary>Returns the path of the preserved copy, or null if it could not be made.</summary>
    private string? PreserveUnreadableWorkspaceFile()
    {
        var path = Path.Combine(_appDataFolder, $"workspace.unreadable-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        try
        {
            File.Copy(_workspaceFilePath, path, overwrite: false);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Saves made while loading (migration, backup restore) are an optimisation - the
    /// in-memory workspace is already correct and the next save from the app writes it - so a
    /// disk failure here must not stop the app from starting.</summary>
    private void TrySaveDuringLoad(Workspace workspace)
    {
        try
        {
            SaveWorkspace(workspace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
        }
    }

    public void SaveWorkspace(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if ((UnsupportedSchemaVersion ?? ReadFutureSchemaVersion()) is { } blockedVersion)
        {
            UnsupportedSchemaVersion = blockedVersion;
            throw new UnsupportedWorkspaceVersionException(blockedVersion);
        }

        WorkspaceMigrator.Normalize(workspace);
        Directory.CreateDirectory(_appDataFolder);

        var json = JsonSerializer.Serialize(workspace, _options);
        File.WriteAllText(_temporaryFilePath, json);

        try
        {
            if (File.Exists(_workspaceFilePath))
            {
                File.Replace(_temporaryFilePath, _workspaceFilePath, _backupFilePath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(_temporaryFilePath, _workspaceFilePath);
                File.Copy(_workspaceFilePath, _backupFilePath, overwrite: true);
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

    /// <summary>The schema version of the main file when it is newer than this build supports;
    /// null for a supported, missing, or unparseable file (unparseable stays on the corruption path).</summary>
    private int? ReadFutureSchemaVersion()
    {
        try
        {
            using var stream = new FileStream(_workspaceFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!string.Equals(property.Name, nameof(Workspace.SchemaVersion), StringComparison.OrdinalIgnoreCase)) continue;
                return property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var version) &&
                       version > Workspace.CurrentSchemaVersion
                    ? version
                    : null;
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private bool TryLoad(string path, out Workspace workspace, out string error)
    {
        try
        {
            var json = File.ReadAllText(path);
            workspace = JsonSerializer.Deserialize<Workspace>(json, _options) ?? new Workspace();
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or CryptographicException)
        {
            workspace = new Workspace();
            error = ex.Message;
            return false;
        }
    }
}
