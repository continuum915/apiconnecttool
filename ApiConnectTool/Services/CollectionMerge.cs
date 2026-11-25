using System;
using System.Collections.Generic;
using System.Linq;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

/// <summary>What importing into an existing collection did, for the log and the summary.</summary>
public sealed record MergeResult(int Created, int Merged, int Overwritten, int Skipped, IReadOnlyList<Guid> OverwrittenRequestIds,
    IReadOnlyList<string> Log);

/// <summary>Imports a parsed source into an existing collection. Folders match by path and
/// requests by name within the same folder, both ignoring case; collection variables match by
/// key and collection environments by name (ignoring case). A matching folder merges. A matching
/// request, variable or environment is skipped, or replaced in place when overwriting, keeping
/// its ID (so open tabs, history links and run selections stay valid) and, for an environment,
/// whether it is active. Values keep their protected (secret) form.</summary>
public static class CollectionMerge
{
    /// <summary>The action for every item of <paramref name="source"/>, by the item's ID.</summary>
    public static IReadOnlyDictionary<Guid, ImportAction> Plan(RequestCollection source, RequestCollection target, bool overwrite)
    {
        var actions = new Dictionary<Guid, ImportAction>();
        var folderMap = MapFolders(source, target);
        foreach (var folder in source.Folders)
            actions[folder.Id] = folderMap.TryGetValue(folder.Id, out var match) && match.Existing ? ImportAction.Merge : ImportAction.New;
        foreach (var request in source.Requests)
            actions[request.Id] = FindRequest(request, target, folderMap) is null ? ImportAction.New : Existing(overwrite);
        foreach (var variable in source.Variables)
            actions[variable.Id] = target.Variables.Any(existing => string.Equals(existing.Key, variable.Key, StringComparison.OrdinalIgnoreCase))
                ? Existing(overwrite) : ImportAction.New;
        foreach (var environment in source.Environments)
            actions[environment.Id] = FindEnvironment(environment, target) is null ? ImportAction.New : Existing(overwrite);
        return actions;
    }

    private static ImportAction Existing(bool overwrite) => overwrite ? ImportAction.Overwrite : ImportAction.Skip;

    /// <summary>Applies the included items of <paramref name="source"/> to <paramref name="target"/>.
    /// Items not in <paramref name="includedIds"/>, and items inside an excluded folder, are skipped.</summary>
    public static MergeResult Apply(RequestCollection source, RequestCollection target, IReadOnlySet<Guid> includedIds,
        bool overwrite, bool importEnvironments)
    {
        var created = 0; var merged = 0; var overwritten = 0; var skipped = 0;
        var log = new List<string>();
        var overwrittenRequests = new List<Guid>();
        var folderMap = MapFolders(source, target);
        var targetFolderIds = new Dictionary<Guid, Guid>();

        foreach (var folder in TopDown(source))
        {
            var included = CollectionImport.IsIncluded(folder.Id, source, includedIds);
            var match = folderMap[folder.Id];
            if (!included) { skipped++; continue; }
            if (match.Existing)
            {
                targetFolderIds[folder.Id] = match.TargetId;
                merged++;
                log.Add(Format("Str.ImportWizard.Log.FolderMerged", folder.Name));
                continue;
            }
            var copy = new CollectionFolder
            {
                Name = folder.Name, Auth = folder.Auth,
                ParentFolderId = folder.ParentFolderId is { } parent && targetFolderIds.TryGetValue(parent, out var mapped) ? mapped : null,
                SortOrder = NextSortOrder(target.Folders.Select(item => item.SortOrder))
            };
            target.Folders.Add(copy);
            targetFolderIds[folder.Id] = copy.Id;
            created++;
            log.Add(Format("Str.ImportWizard.Log.Folder", folder.Name));
        }

        foreach (var request in source.Requests)
        {
            var folderIncluded = request.FolderId is not { } folderId || CollectionImport.IsIncluded(folderId, source, includedIds);
            if (!includedIds.Contains(request.Id) || !folderIncluded) { skipped++; continue; }
            var label = $"{request.Method.Method} {request.Name}";
            if (FindRequest(request, target, folderMap) is { } existing)
            {
                if (!overwrite) { skipped++; log.Add(Format("Str.ImportWizard.Log.Skipped", label)); continue; }
                var replacement = request.CloneForEditing();
                replacement.Id = existing.Id;
                replacement.FolderId = existing.FolderId;
                replacement.SortOrder = existing.SortOrder;
                target.Requests[target.Requests.IndexOf(existing)] = replacement;
                overwrittenRequests.Add(existing.Id);
                overwritten++;
                log.Add(Format("Str.ImportWizard.Log.Overwritten", label));
                continue;
            }
            var copy = request.CloneForEditing();
            copy.Id = Guid.NewGuid();
            copy.FolderId = request.FolderId is { } sourceFolder && targetFolderIds.TryGetValue(sourceFolder, out var targetFolder) ? targetFolder : null;
            copy.SortOrder = NextSortOrder(target.Requests.Where(item => item.FolderId == copy.FolderId).Select(item => item.SortOrder));
            target.Requests.Add(copy);
            created++;
            log.Add(Format("Str.ImportWizard.Log.Request", label));
        }

        foreach (var variable in source.Variables)
        {
            if (!includedIds.Contains(variable.Id)) { skipped++; continue; }
            var existing = target.Variables.FirstOrDefault(item => string.Equals(item.Key, variable.Key, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                var copy = variable.Clone();
                target.Variables.Add(copy);
                created++;
                log.Add(Format("Str.ImportWizard.Log.Variable", variable.Key));
            }
            else if (overwrite)
            {
                CopyValues(variable, existing);
                overwritten++;
                log.Add(Format("Str.ImportWizard.Log.VariableOverwritten", variable.Key));
            }
            else skipped++;
        }

        foreach (var environment in source.Environments)
        {
            if (!importEnvironments || !includedIds.Contains(environment.Id)) { skipped++; continue; }
            if (FindEnvironment(environment, target) is { } existing)
            {
                if (!overwrite) { skipped++; continue; }
                existing.IsProduction = environment.IsProduction;
                existing.Variables.Clear();
                foreach (var variable in environment.Variables) existing.Variables.Add(variable.Clone());
                overwritten++;
                log.Add(Format("Str.ImportWizard.Log.EnvironmentOverwritten", environment.Name));
                continue;
            }
            var copy = new RequestEnvironment { Name = environment.Name, IsProduction = environment.IsProduction };
            foreach (var variable in environment.Variables) copy.Variables.Add(variable.Clone());
            target.Environments.Add(copy);
            created++;
            log.Add(Format("Str.ImportWizard.Log.Environment", environment.Name));
        }

        return new MergeResult(created, merged, overwritten, skipped, overwrittenRequests, log);
    }

    private static void CopyValues(EnvironmentVariable from, EnvironmentVariable to)
    {
        to.InitialValue = from.InitialValue;
        to.CurrentValue = from.CurrentValue;
        to.HasCurrentValue = from.HasCurrentValue;
        to.IsSecret = from.IsSecret;
        to.IsEnabled = from.IsEnabled;
    }

    private readonly record struct FolderMatch(Guid TargetId, bool Existing);

    /// <summary>Maps each source folder to the target folder with the same path (ignoring case),
    /// when one exists.</summary>
    private static Dictionary<Guid, FolderMatch> MapFolders(RequestCollection source, RequestCollection target)
    {
        var targetByPath = target.Folders.GroupBy(folder => PathOf(folder, target), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);
        return source.Folders.ToDictionary(folder => folder.Id, folder =>
            targetByPath.TryGetValue(PathOf(folder, source), out var id) ? new FolderMatch(id, true) : new FolderMatch(Guid.Empty, false));
    }

    private static RequestItem? FindRequest(RequestItem request, RequestCollection target, IReadOnlyDictionary<Guid, FolderMatch> folderMap)
    {
        Guid? targetFolder;
        if (request.FolderId is not { } folderId) targetFolder = null;
        else if (folderMap.TryGetValue(folderId, out var match) && match.Existing) targetFolder = match.TargetId;
        else return null;
        return target.Requests.FirstOrDefault(item => item.FolderId == targetFolder
            && string.Equals(item.Name.Trim(), request.Name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static RequestEnvironment? FindEnvironment(RequestEnvironment environment, RequestCollection target) =>
        target.Environments.FirstOrDefault(item => string.Equals(item.Name.Trim(), environment.Name.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string PathOf(CollectionFolder folder, RequestCollection collection)
    {
        var parts = new List<string>();
        var visited = new HashSet<Guid>();
        for (var current = folder; current is not null && visited.Add(current.Id);
             current = current.ParentFolderId is { } parent ? collection.Folders.FirstOrDefault(item => item.Id == parent) : null)
            parts.Insert(0, current.Name.Trim());
        return string.Join("/", parts);
    }

    private static IEnumerable<CollectionFolder> TopDown(RequestCollection collection)
    {
        var emitted = new HashSet<Guid>();
        IEnumerable<CollectionFolder> Children(Guid? parent)
        {
            foreach (var folder in collection.Folders.Where(folder => folder.ParentFolderId == parent).OrderBy(folder => folder.SortOrder))
            {
                if (!emitted.Add(folder.Id)) continue;
                yield return folder;
                foreach (var child in Children(folder.Id)) yield return child;
            }
        }
        return Children(null).ToArray();
    }

    private static int NextSortOrder(IEnumerable<int> existing) => existing.DefaultIfEmpty(-1).Max() + 1;

    private static string Format(string key, string value) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, Localization.DefaultStrings.Get(key), value);
}
