using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Services;

/// <summary>The import formats the wizard reads.</summary>
public enum ImportFormat { ApiConnectTool, Postman, OpenApi, Curl }

/// <summary>What one review row will do when the import starts.</summary>
public enum ImportAction { New, Merge, Skip, Overwrite }

public enum ImportItemKind { Folder, Request, Environment, Variable }

/// <summary>A parsed import source, before anything is written: the collection the importer
/// produced (fresh IDs, not added to the workspace) and what it holds.</summary>
public sealed record ImportPreview(ImportFormat Format, RequestCollection Collection, string? Notes)
{
    public int FolderCount => Collection.Folders.Count;
    public int RequestCount => Collection.Requests.Count;
    public int EnvironmentCount => Collection.Environments.Count;
    public int VariableCount => Collection.Variables.Count;

    /// <summary>The detection note shown on the Source step.</summary>
    public string DetectionSummary => string.Format(CultureInfo.CurrentCulture, DefaultStrings.Get("Str.ImportWizard.Detected"),
        DefaultStrings.Get("Str.ImportWizard.Format." + Format), FolderCount, RequestCount, EnvironmentCount, VariableCount);
}

/// <summary>Reads import sources into previews and commits a reviewed selection. Reading never
/// changes the workspace; only <see cref="Commit"/> produces the collection to add.</summary>
public sealed class CollectionImport(ICollectionExportService? apiconnecttool = null, IPostmanCollectionConverter? postman = null,
    IOpenApiImporter? openApi = null, ICurlImporter? curl = null)
{
    private readonly ICollectionExportService _apiconnecttool = apiconnecttool ?? new CollectionExportService();
    private readonly IPostmanCollectionConverter _postman = postman ?? new PostmanCollectionConverter();
    private readonly IOpenApiImporter _openApi = openApi ?? new OpenApiImporter();
    private readonly ICurlImporter _curl = curl ?? new CurlImporter();

    /// <summary>Parses <paramref name="text"/> as <paramref name="format"/>. Throws the importer's
    /// own exception (<see cref="InvalidDataException"/>, <see cref="PostmanImportException"/>,
    /// <see cref="OpenApiImportException"/>, <see cref="CurlImportException"/>) when it cannot.</summary>
    public ImportPreview Read(ImportFormat format, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException(DefaultStrings.Get("Str.ImportWizard.EmptySource"));
        switch (format)
        {
            case ImportFormat.ApiConnectTool:
                return new(format, _apiconnecttool.Import(text), null);
            case ImportFormat.Postman:
            {
                var result = _postman.ImportWithReport(text);
                var notes = result.Report.ToUserMessage();
                return new(format, result.Collection, notes.Length == 0 ? null : notes);
            }
            case ImportFormat.OpenApi:
            {
                var result = _openApi.ImportWithReport(text);
                return new(format, result.Collection, result.Report.HasWarnings ? result.Report.ToUserMessage() : null);
            }
            default:
            {
                var request = _curl.Import(text);
                var collection = new RequestCollection { Name = DefaultStrings.Get("Str.ImportWizard.CurlCollectionName") };
                request.FolderId = null;
                collection.Requests.Add(request);
                return new(format, collection, null);
            }
        }
    }

    /// <summary>The new collection to add: the preview's content limited to the included folders
    /// and requests (a request in an excluded folder is excluded too), named
    /// <paramref name="name"/>. Collection environments are kept only when
    /// <paramref name="importEnvironments"/> is set, and none is active.</summary>
    public static RequestCollection Commit(ImportPreview preview, string name, bool importEnvironments,
        IReadOnlySet<Guid> includedIds)
    {
        var source = preview.Collection;
        var includedFolders = source.Folders.Where(folder => IsIncluded(folder.Id, source, includedIds)).Select(folder => folder.Id).ToHashSet();
        var result = new RequestCollection
        {
            Id = source.Id, Name = name, Auth = source.Auth, Variables = source.Variables,
            Folders = new(source.Folders.Where(folder => includedFolders.Contains(folder.Id))),
            Requests = new(source.Requests.Where(request => includedIds.Contains(request.Id)
                && (request.FolderId is not { } folderId || includedFolders.Contains(folderId)))),
            Environments = importEnvironments ? source.Environments : new()
        };
        result.ActiveEnvironmentId = null;
        return result;
    }

    /// <summary>A folder is included when it and every folder above it are included.</summary>
    public static bool IsIncluded(Guid folderId, RequestCollection collection, IReadOnlySet<Guid> includedIds)
    {
        var visited = new HashSet<Guid>();
        for (Guid? current = folderId; current is { } id && visited.Add(id);
             current = collection.Folders.FirstOrDefault(folder => folder.Id == id)?.ParentFolderId)
            if (!includedIds.Contains(id)) return false;
        return true;
    }
}
