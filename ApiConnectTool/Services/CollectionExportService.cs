using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using ApiConnectTool.Models;
using ApiConnectTool.Serialization;

namespace ApiConnectTool.Services;

public sealed class CollectionExportService : ICollectionExportService
{
    public const int CurrentExportSchemaVersion = 4;

    public string Export(RequestCollection collection, Guid sourceWorkspaceId, bool includeSecrets)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var envelope = new ApiConnectToolCollectionExport
        {
            ExportSchemaVersion = CurrentExportSchemaVersion,
            ExportedAtUtc = DateTimeOffset.UtcNow,
            SourceWorkspaceId = sourceWorkspaceId,
            IncludesSecrets = includeSecrets,
            Collection = collection
        };

        var options = BuildOptions(includeSecrets);
        var document = JsonSerializer.SerializeToNode(envelope, options)!;
        // The active environment is a local choice: an imported collection must never start out
        // targeting a stage the importer did not pick.
        document[nameof(ApiConnectToolCollectionExport.Collection)]!.AsObject().Remove(nameof(RequestCollection.ActiveEnvironmentId));
        return document.ToJsonString(options);
    }

    public RequestCollection Import(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var exportedVersion = ReadSchemaVersion(json);
        if (exportedVersion > CurrentExportSchemaVersion)
        {
            throw new InvalidDataException(
                $"This file was exported by a newer version of ApiConnectTool (export schema {exportedVersion}) " +
                $"than this build supports (export schema {CurrentExportSchemaVersion}).");
        }

        RequestCollection? imported;
        try
        {
            // Reading never needs to hide anything - whatever plaintext is present (including a
            // deliberately blank default-export value) is taken as-is.
            var envelope = JsonSerializer.Deserialize<ApiConnectToolCollectionExport>(json, BuildOptions(includeSecrets: true));
            imported = envelope?.Collection;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The file is not a valid ApiConnectTool export.", ex);
        }

        if (imported is null)
        {
            throw new InvalidDataException("The file is not a valid ApiConnectTool export.");
        }

        return RemapWithFreshIds(imported);
    }

    private static int ReadSchemaVersion(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("ExportSchemaVersion", out var versionElement) &&
                versionElement.ValueKind == JsonValueKind.Number)
            {
                return versionElement.GetInt32();
            }
        }
        catch (JsonException)
        {
        }

        throw new InvalidDataException("The file is not a valid ApiConnectTool export.");
    }

    private static JsonSerializerOptions BuildOptions(bool includeSecrets) => new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new ExportVariableJsonConverter(includeSecrets), new PlainEncryptedStringJsonConverter(includeSecrets), new HttpMethodJsonConverter() }
    };

    /// <summary>
    /// Mirrors CollectionsViewModel.DuplicateCollection (4b): assigns a fresh Id to the collection,
    /// every folder, every request, and every collection environment, remapping
    /// FolderId/ParentFolderId references to match and leaving no environment active -
    /// so importing a file back into the workspace it was exported from can never collide with
    /// the source requests.
    /// </summary>
    private static RequestCollection RemapWithFreshIds(RequestCollection source)
    {
        var folderIdMap = new Dictionary<Guid, Guid>();
        var folders = new ObservableCollection<CollectionFolder>();
        foreach (var folder in source.Folders)
        {
            var copy = new CollectionFolder { Name = folder.Name, SortOrder = folder.SortOrder, Auth = folder.Auth.Clone(false) };
            folderIdMap[folder.Id] = copy.Id;
            folders.Add(copy);
        }

        foreach (var folder in folders)
        {
            var original = source.Folders.First(f => folderIdMap[f.Id] == folder.Id);
            folder.ParentFolderId = original.ParentFolderId is { } parentId && folderIdMap.TryGetValue(parentId, out var mappedParentId)
                ? mappedParentId
                : null;
        }

        var requests = new ObservableCollection<RequestItem>();
        foreach (var request in source.Requests)
        {
            var copy = request.Clone();
            copy.FolderId = request.FolderId is { } folderId && folderIdMap.TryGetValue(folderId, out var mappedFolderId)
                ? mappedFolderId
                : null;
            requests.Add(copy);
        }

        return new RequestCollection
        {
            Name = source.Name,
            Auth = source.Auth.Clone(false),
            Variables = new(source.Variables.Select(variable => variable.Clone())),
            Environments = new(source.Environments.Select(environment => new RequestEnvironment
            {
                Name = environment.Name,
                IsProduction = environment.IsProduction,
                Variables = new(environment.Variables.Select(variable => variable.Clone()))
            })),
            Folders = folders,
            Requests = requests
        };
    }
}
