using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApiConnectTool.Models;
using ApiConnectTool.Services;

namespace ApiConnectTool.ViewModels;

/// <summary>
/// Backs the sidebar's Collections panel: the workspace's collections, their tree projection,
/// and sidebar search. Owned by <see cref="MainWindowViewModel"/> the same way
/// <see cref="HistoryViewModel"/> is, and holds no reference back to it - everything that crosses
/// the boundary (persisting, opening a tab, closing tabs of removed requests, retitling tabs of
/// renamed requests, the Duplicate naming prompt) comes in through the constructor.
/// </summary>
public partial class CollectionsViewModel : ObservableObject
{
    private readonly ICollectionExportService _collectionExportService;
    private readonly ICurlImporter _curlImporter;
    private readonly IPostmanCollectionConverter _postmanCollectionConverter;
    private readonly IOpenApiImporter _openApiImporter;
    private readonly Func<Guid> _workspaceId;
    private readonly Func<bool> _persist;
    private readonly Action<RequestItem> _openRequest;
    private readonly Action<IReadOnlyCollection<Guid>> _requestsRemoved;
    private readonly Func<string, Task<string?>> _resolveDuplicateName;
    private readonly Action<RequestItem> _requestRenamed;

    [ObservableProperty] private string? _importExportMessage;
    [ObservableProperty] private string _searchText = string.Empty;
    /// <summary>The navigator's selected node: a CollectionTreeNode, FolderTreeNode or RequestItem.</summary>
    [ObservableProperty] private object? _selectedNode;

    public ObservableCollection<RequestCollection> Collections { get; }
    public ObservableCollection<CollectionTreeNode> Tree { get; } = new();
    public ObservableCollection<RequestItem> SearchResults { get; } = new();
    public bool IsSearchActive => !string.IsNullOrWhiteSpace(SearchText);

    /// <param name="collections">The workspace's collection list, used (and mutated) in place.</param>
    /// <param name="persist">Saves the workspace; returns false when the write failed.</param>
    /// <param name="openRequest">Opens (or activates) a tab for a request.</param>
    /// <param name="requestsRemoved">Told the ids of requests that no longer exist, so their tabs
    /// can be closed.</param>
    /// <param name="resolveDuplicateName">Asks for a duplicate's name given the proposed default;
    /// null means cancelled. When unset, the default is accepted.</param>
    /// <param name="requestRenamed">Told a saved request was renamed, so its open tabs can take
    /// the new name.</param>
    /// <param name="workspaceId">Supplies the current source workspace id for native exports.</param>
    public CollectionsViewModel(
        ObservableCollection<RequestCollection> collections,
        ICollectionExportService? collectionExportService = null,
        ICurlImporter? curlImporter = null,
        IPostmanCollectionConverter? postmanCollectionConverter = null,
        IOpenApiImporter? openApiImporter = null,
        Func<bool>? persist = null,
        Action<RequestItem>? openRequest = null,
        Action<IReadOnlyCollection<Guid>>? requestsRemoved = null,
        Func<string, Task<string?>>? resolveDuplicateName = null,
        Action<RequestItem>? requestRenamed = null,
        Func<Guid>? workspaceId = null)
    {
        Collections = collections ?? throw new ArgumentNullException(nameof(collections));
        _collectionExportService = collectionExportService ?? new CollectionExportService();
        _curlImporter = curlImporter ?? new CurlImporter();
        _postmanCollectionConverter = postmanCollectionConverter ?? new PostmanCollectionConverter();
        _openApiImporter = openApiImporter ?? new OpenApiImporter();
        _workspaceId = workspaceId ?? (() => Guid.Empty);
        _persist = persist ?? (() => true);
        _openRequest = openRequest ?? (_ => { });
        _requestsRemoved = requestsRemoved ?? (_ => { });
        _resolveDuplicateName = resolveDuplicateName ?? (name => Task.FromResult<string?>(name));
        _requestRenamed = requestRenamed ?? (_ => { });
        Refresh();
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsSearchActive));
        RefreshSearchResults();
    }

    /// <summary>Brings the tree and search results up to date with <see cref="Collections"/>.
    /// Called after every change to collection structure or content (including tab saves made
    /// outside this view model), so neither goes stale.</summary>
    public void Refresh()
    {
        RefreshTree();
        RefreshSearchResults();
    }

    /// <summary>Every place a request can be saved or moved to, in sidebar order: each
    /// collection's root followed by its folders depth-first.</summary>
    public IReadOnlyList<SaveDestination> GetSaveDestinations()
    {
        var destinations = new List<SaveDestination>();
        foreach (var collection in Collections)
        {
            destinations.Add(new SaveDestination(collection, null, collection.Name, collection.Name, 0));
            AddFolderDestinations(collection, null, collection.Name, 1, destinations);
        }

        return destinations;
    }

    private static void AddFolderDestinations(RequestCollection collection, Guid? parentFolderId, string parentPath, int depth, List<SaveDestination> destinations)
    {
        foreach (var folder in collection.Folders.Where(folder => folder.ParentFolderId == parentFolderId).OrderBy(folder => folder.SortOrder))
        {
            var path = $"{parentPath} / {folder.Name}";
            destinations.Add(new SaveDestination(collection, folder.Id, path, folder.Name, depth));
            AddFolderDestinations(collection, folder.Id, path, depth + 1, destinations);
        }
    }

    public SaveDestination? DestinationOf(RequestItem request, IReadOnlyList<SaveDestination> destinations)
    {
        var collection = Collections.FirstOrDefault(candidate => candidate.Requests.Contains(request));
        return destinations.FirstOrDefault(destination =>
            ReferenceEquals(destination.Collection, collection) && destination.FolderId == request.FolderId);
    }

    /// <summary>Set by the View to ask where to move a request: gets the request, every
    /// destination, and its current one; returns the chosen destination or null to cancel. When
    /// unset (e.g. in tests), Move to... does nothing.</summary>
    public Func<RequestItem, IReadOnlyList<SaveDestination>, SaveDestination?, Task<SaveDestination?>>? MoveDestinationPromptHandler { get; set; }

    /// <summary>Set by the View to confirm a sidebar delete by item kind and display name.
    /// An unset handler refuses deletion.</summary>
    public Func<CollectionItemKind, string, Task<bool>>? DeleteConfirmationHandler { get; set; }

    /// <summary>Sidebar "Move to...": asks for a collection/folder and moves the request to the
    /// end of it. Picking the location it's already in is a no-op.</summary>
    [RelayCommand]
    private async Task MoveRequestToAsync(RequestItem? request)
    {
        if (request is null || MoveDestinationPromptHandler is null)
        {
            return;
        }

        var destinations = GetSaveDestinations();
        var current = DestinationOf(request, destinations);
        var chosen = await MoveDestinationPromptHandler(request, destinations, current);
        if (chosen is null || chosen.IsSameLocation(current))
        {
            return;
        }

        MoveRequest(request, chosen.Collection, chosen.FolderId, int.MaxValue);
    }

    [RelayCommand]
    private void MoveRequestUp(RequestItem? request) => MoveRequestWithinSiblings(request, -1);

    [RelayCommand]
    private void MoveRequestDown(RequestItem? request) => MoveRequestWithinSiblings(request, +1);

    private void MoveRequestWithinSiblings(RequestItem? request, int offset)
    {
        var collection = request is null ? null : Collections.FirstOrDefault(candidate => candidate.Requests.Contains(request));
        if (request is null || collection is null)
        {
            return;
        }

        var siblings = collection.Requests.Where(r => r.FolderId == request.FolderId).OrderBy(r => r.SortOrder).ToList();
        var targetIndex = siblings.IndexOf(request) + offset;
        if (targetIndex < 0 || targetIndex >= siblings.Count)
        {
            return;
        }

        MoveRequest(request, collection, request.FolderId, targetIndex);
    }

    /// <summary>
    /// Moves a request to a (possibly different) collection and/or folder, inserting it at
    /// <paramref name="targetIndex"/> among its new siblings (requests sharing the same
    /// collection + FolderId) and renumbering that sibling group's SortOrder to match - the same
    /// path handles a cross-collection move, a cross-folder move, and a plain same-folder
    /// reorder. Backs the sidebar's Move to... / Move Up / Move Down actions.
    /// </summary>
    public void MoveRequest(RequestItem request, RequestCollection targetCollection, Guid? targetFolderId, int targetIndex)
    {
        var sourceCollection = Collections.FirstOrDefault(candidate => candidate.Requests.Contains(request));
        if (sourceCollection is null)
        {
            return;
        }

        var sourceFolderId = request.FolderId;
        sourceCollection.Requests.Remove(request);
        request.FolderId = targetFolderId;

        if (!targetCollection.Requests.Contains(request))
        {
            targetCollection.Requests.Add(request);
        }

        RenumberSiblingSortOrders(targetCollection, targetFolderId, request, targetIndex);
        if (!ReferenceEquals(sourceCollection, targetCollection) || sourceFolderId != targetFolderId)
        {
            // Close the gap the request left behind in its old sibling group.
            RenumberSiblingSortOrders(sourceCollection, sourceFolderId);
        }

        Commit();
    }

    /// <summary>Places a newly saved snapshot without persisting. The caller owns the disk
    /// write and can invoke the returned action to undo placement if that write fails.</summary>
    public Action PlaceSavedRequest(RequestItem snapshot, string desiredName, SaveDestination? destination)
    {
        var collection = destination?.Collection ?? Collections.FirstOrDefault();
        var createdCollection = collection is null;
        if (collection is null)
        {
            collection = new RequestCollection { Name = "Requests" };
            Collections.Add(collection);
        }

        snapshot.Name = NextRequestName(collection, desiredName);
        snapshot.FolderId = destination?.FolderId;
        snapshot.SortOrder = collection.Requests.Count(request => request.FolderId == snapshot.FolderId);
        collection.Requests.Add(snapshot);
        Refresh();
        return () =>
        {
            collection.Requests.Remove(snapshot);
            if (createdCollection)
            {
                Collections.Remove(collection);
            }
            Refresh();
        };
    }

    /// <summary>Serializes a collection to the native ApiConnectTool export format (pure - the caller owns
    /// writing the returned text to a file).</summary>
    public string ExportCollection(RequestCollection collection, bool includeSecrets) =>
        _collectionExportService.Export(collection, _workspaceId(), includeSecrets);

    /// <summary>
    /// Parses a native ApiConnectTool export file and adds it as a new collection (fresh IDs throughout,
    /// name disambiguated against existing collections the same way AddCollection/
    /// DuplicateCollection already do) - never overwrites or merges into an existing collection.
    /// Throws <see cref="System.IO.InvalidDataException"/> for a malformed or too-new file; the
    /// caller is expected to catch and surface that.
    /// </summary>
    public void ImportCollection(string json)
    {
        var imported = _collectionExportService.Import(json);
        AddImportedCollection(imported);
    }

    /// <summary>
    /// Parses a pasted curl command and opens it as a new unsaved tab - the same "always a new
    /// tab, never reuses one already open" behavior as ResendFromHistory, since a freshly parsed
    /// request has no ties to any saved collection item yet. Throws
    /// <see cref="CurlImportException"/> for malformed/unsupported input; the caller is expected
    /// to catch and surface that.
    /// </summary>
    public void ImportFromCurl(string command)
    {
        var request = _curlImporter.Import(command);
        _openRequest(request);
    }

    /// <summary>Serializes a collection to a Postman Collection v2.1 document (pure - the caller
    /// owns writing the returned text to a file).</summary>
    public string ExportCollectionAsPostman(RequestCollection collection, bool includeSecrets) =>
        _postmanCollectionConverter.Export(collection, includeSecrets);

    /// <summary>The note to show after a Postman export, which cannot carry collection
    /// environments, or null when the collection has none.</summary>
    public static string? PostmanExportNote(RequestCollection collection) => collection.Environments.Count == 0 ? null
        : string.Format(System.Globalization.CultureInfo.CurrentCulture,
            ApiConnectTool.Services.Localization.AppLocalization.Get("Str.Export.PostmanEnvironmentsOmitted"), collection.Environments.Count);

    /// <summary>
    /// Parses a Postman Collection v2.1 document and adds it as a new collection (fresh IDs
    /// throughout, name disambiguated against existing collections), the same shape as
    /// <see cref="ImportCollection"/>. Throws <see cref="PostmanImportException"/> for a document
    /// that isn't a recognizable collection; the caller is expected to catch and surface that.
    /// </summary>
    public PostmanImportReport ImportPostmanCollection(string json)
    {
        var result = _postmanCollectionConverter.ImportWithReport(json);
        AddImportedCollection(result.Collection);
        ImportExportMessage = result.Report.ToUserMessage();
        return result.Report;
    }

    /// <summary>
    /// Parses an OpenAPI 3.x document and adds it as a new collection, the
    /// same shape as <see cref="ImportCollection"/>/<see cref="ImportPostmanCollection"/>. Throws
    /// <see cref="OpenApiImportException"/> for a document that isn't a recognizable OpenAPI spec;
    /// the caller is expected to catch and surface that.
    /// </summary>
    public OpenApiImportReport ImportOpenApiCollection(string json)
    {
        var result = _openApiImporter.ImportWithReport(json);
        var imported = result.Collection;
        AddImportedCollection(imported);
        return result.Report;
    }

    /// <summary>Set by the main window: reloads (or asks about) open tabs whose requests an import
    /// overwrote in place.</summary>
    public Func<IReadOnlyCollection<Guid>, Task>? RequestsReplaced { get; set; }

    /// <summary>Imports <paramref name="source"/> into <paramref name="target"/> by the merge rules,
    /// saves the workspace once, then lets open tabs of overwritten requests reload.</summary>
    public async Task<MergeResult> MergeImportAsync(RequestCollection source, RequestCollection target, IReadOnlySet<Guid> includedIds,
        bool overwrite, bool importEnvironments)
    {
        var result = CollectionMerge.Apply(source, target, includedIds, overwrite, importEnvironments);
        Commit();
        if (result.OverwrittenRequestIds.Count > 0 && RequestsReplaced is { } reload) await reload(result.OverwrittenRequestIds);
        return result;
    }

    /// <summary>Adds an imported collection (its name made unique) and saves the workspace once.</summary>
    public void AddImportedCollection(RequestCollection imported)
    {
        imported.Name = NextName(Collections.Select(c => c.Name), imported.Name);
        Collections.Add(imported);
        Commit();
    }

    [RelayCommand]
    private void AddCollection()
    {
        Collections.Add(new RequestCollection { Name = NextName(Collections.Select(collection => collection.Name), "New Collection") });
        Commit();
    }

    [RelayCommand]
    private async Task DeleteCollectionAsync(RequestCollection? collection)
    {
        if (collection is null || !Collections.Contains(collection) || DeleteConfirmationHandler is null ||
            !await DeleteConfirmationHandler(CollectionItemKind.Collection, collection.Name))
        {
            return;
        }

        _requestsRemoved(collection.Requests.Select(request => request.Id).ToList());
        Collections.Remove(collection);
        Commit();
    }

    [RelayCommand]
    private void AddRequest(RequestCollection? collection)
    {
        if (collection is null)
        {
            return;
        }

        var item = new RequestItem { Name = NextRequestName(collection, "New Request"), SortOrder = collection.Requests.Count };
        collection.Requests.Add(item);
        Commit();
        _openRequest(item);
    }

    [RelayCommand]
    private async Task DeleteRequestAsync(RequestItem? request)
    {
        if (request is null)
        {
            return;
        }

        var collection = Collections.FirstOrDefault(candidate => candidate.Requests.Contains(request));
        if (collection is null || DeleteConfirmationHandler is null ||
            !await DeleteConfirmationHandler(CollectionItemKind.Request, request.Name))
        {
            return;
        }

        _requestsRemoved([request.Id]);
        collection.Requests.Remove(request);
        Commit();
    }

    [RelayCommand]
    private async Task DuplicateRequestAsync(RequestItem? request)
    {
        if (request is null)
        {
            return;
        }

        var collection = Collections.FirstOrDefault(candidate => candidate.Requests.Contains(request));
        if (collection is null)
        {
            return;
        }

        var name = await _resolveDuplicateName($"Copy of {request.Name}");
        if (name is null)
        {
            return;
        }

        var copy = request.Clone(newAssertionIds: true);
        copy.Id = Guid.NewGuid();
        copy.Name = NextRequestName(collection, name);
        copy.FolderId = request.FolderId;

        var insertAt = collection.Requests.IndexOf(request) + 1;
        collection.Requests.Insert(insertAt, copy);
        RenumberSiblingSortOrders(collection, copy.FolderId);
        Commit();
    }

    [RelayCommand]
    private void RenameRequest(RequestItem? request)
    {
        if (request is null)
        {
            return;
        }

        var trimmed = request.Name.Trim();
        request.Name = string.IsNullOrEmpty(trimmed) ? "Untitled Request" : trimmed;
        _requestRenamed(request);
        Commit();
    }

    /// <summary>Reverts an in-progress sidebar rename (Escape) for any renamable entity - kept
    /// here instead of in SidebarControl's code-behind so cancel semantics live alongside the
    /// commit path (RenameCollection/RenameFolder/RenameRequest) rather than mutating model state
    /// directly from the view.</summary>
    [RelayCommand]
    private void CancelRename(IRenamable? item)
    {
        if (item is null)
        {
            return;
        }

        item.Name = item.RenameOriginalName ?? item.Name;
        item.IsEditing = false;
    }

    [RelayCommand]
    private void AddFolder(RequestCollection? collection)
    {
        if (collection is null)
        {
            return;
        }

        var topLevelNames = collection.Folders.Where(f => f.ParentFolderId is null).Select(f => f.Name);
        var folder = new CollectionFolder
        {
            Name = NextName(topLevelNames, "New Folder"),
            SortOrder = collection.Folders.Count(f => f.ParentFolderId is null)
        };
        collection.Folders.Add(folder);
        Commit();
    }

    [RelayCommand]
    private void RenameFolder(CollectionFolder? folder)
    {
        if (folder is null)
        {
            return;
        }

        var trimmed = folder.Name.Trim();
        folder.Name = string.IsNullOrEmpty(trimmed) ? "Untitled Folder" : trimmed;
        Commit();
    }

    /// <summary>
    /// Deleting a folder never deletes its contents - requests inside it move to the collection
    /// root and any (currently UI-unreachable, since AddFolder only creates top-level folders)
    /// subfolders are promoted to top-level, matching the "don't silently destroy
    /// user data" posture used elsewhere in this codebase (e.g. workspace write recovery).
    /// </summary>
    [RelayCommand]
    private async Task DeleteFolderAsync(CollectionFolder? folder)
    {
        if (folder is null)
        {
            return;
        }

        var collection = Collections.FirstOrDefault(candidate => candidate.Folders.Contains(folder));
        if (collection is null || DeleteConfirmationHandler is null ||
            !await DeleteConfirmationHandler(CollectionItemKind.Folder, folder.Name))
        {
            return;
        }

        foreach (var request in collection.Requests.Where(r => r.FolderId == folder.Id))
        {
            request.FolderId = null;
        }

        foreach (var childFolder in collection.Folders.Where(f => f.ParentFolderId == folder.Id))
        {
            childFolder.ParentFolderId = null;
        }

        collection.Folders.Remove(folder);
        RenumberSiblingSortOrders(collection, null);
        Commit();
    }

    [RelayCommand]
    private void RenameCollection(RequestCollection? collection)
    {
        if (collection is null)
        {
            return;
        }

        var trimmed = collection.Name.Trim();
        collection.Name = string.IsNullOrEmpty(trimmed) ? "Untitled Collection" : trimmed;
        Commit();
    }

    [RelayCommand]
    private void DuplicateCollection(RequestCollection? collection)
    {
        if (collection is null)
        {
            return;
        }

        var copy = new RequestCollection { Name = NextName(Collections.Select(c => c.Name), $"Copy of {collection.Name}"),
            Auth = collection.Auth.Clone(false), Variables = new(collection.Variables.Select(variable => variable.Clone())) };
        foreach (var environment in collection.Environments)
        {
            var environmentCopy = new RequestEnvironment { Name = environment.Name, IsProduction = environment.IsProduction,
                Variables = new(environment.Variables.Select(variable => variable.Clone())) };
            copy.Environments.Add(environmentCopy);
            if (collection.ActiveEnvironmentId == environment.Id) copy.ActiveEnvironmentId = environmentCopy.Id;
        }
        var folderIdMap = new Dictionary<Guid, Guid>();
        foreach (var folder in collection.Folders)
        {
            var folderCopy = new CollectionFolder { Name = folder.Name, SortOrder = folder.SortOrder, Auth = folder.Auth.Clone(false) };
            folderIdMap[folder.Id] = folderCopy.Id;
            copy.Folders.Add(folderCopy);
        }
        foreach (var folderCopy in copy.Folders)
        {
            var original = collection.Folders.First(f => folderIdMap[f.Id] == folderCopy.Id);
            folderCopy.ParentFolderId = original.ParentFolderId is { } parentId && folderIdMap.TryGetValue(parentId, out var mappedParentId)
                ? mappedParentId
                : null;
        }

        foreach (var request in collection.Requests)
        {
            var requestCopy = request.Clone(newAssertionIds: true);
            requestCopy.Id = Guid.NewGuid();
            requestCopy.FolderId = request.FolderId is { } folderId && folderIdMap.TryGetValue(folderId, out var mappedFolderId) ? mappedFolderId : null;
            copy.Requests.Add(requestCopy);
        }

        Collections.Add(copy);
        Commit();
    }

    /// <summary>Brings the tree and search up to date, then persists. A failed write is surfaced
    /// by the persist callback itself (the workspace save error), and the change stays in memory
    /// either way, so the result is not needed here.</summary>
    private void Commit()
    {
        Refresh();
        _persist();
    }

    /// <summary>
    /// Renumbers SortOrder (0, 1, 2, ...) for every request in <paramref name="collection"/>
    /// sharing <paramref name="folderId"/>, ordered by current SortOrder. When
    /// <paramref name="pinnedItem"/> is given, it's pulled out of that order first and reinserted
    /// at <paramref name="pinnedIndex"/> before renumbering - used to place a moved/duplicated
    /// item at a specific position among its new siblings rather than wherever its prior
    /// SortOrder would otherwise sort it.
    /// </summary>
    internal static void RenumberSiblingSortOrders(RequestCollection collection, Guid? folderId, RequestItem? pinnedItem = null, int pinnedIndex = 0)
    {
        var siblings = collection.Requests.Where(r => r.FolderId == folderId).OrderBy(r => r.SortOrder).ToList();
        if (pinnedItem is not null)
        {
            siblings.Remove(pinnedItem);
            siblings.Insert(Math.Clamp(pinnedIndex, 0, siblings.Count), pinnedItem);
        }

        for (var i = 0; i < siblings.Count; i++)
        {
            siblings[i].SortOrder = i;
        }
    }

    internal static string NextRequestName(RequestCollection collection, string baseName) =>
        NextName(collection.Requests.Select(request => request.Name), baseName);

    internal static string NextName(IEnumerable<string> existingNames, string baseName)
    {
        var names = existingNames.ToHashSet();
        var name = baseName;
        var suffix = 1;
        while (names.Contains(name))
        {
            name = $"{baseName} {suffix++}";
        }

        return name;
    }

    /// <summary>
    /// Case-insensitive substring match over name, URL, and method, across every collection -
    /// no fuzzy search needed for v1. Recomputed on every keystroke and whenever the sidebar tree
    /// itself changes shape (add/delete/move) so results don't go stale mid-search.
    /// </summary>
    private void RefreshSearchResults()
    {
        SearchResults.Clear();
        var query = SearchText?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            return;
        }

        foreach (var request in Collections.SelectMany(collection => collection.Requests)
                     .Where(request => Matches(request, query)))
        {
            SearchResults.Add(request);
        }
    }

    private static bool Matches(RequestItem request, string query) =>
        request.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        request.Url.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        request.Method.Method.Contains(query, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reconciles Tree against Collections in place, reusing existing
    /// CollectionTreeNode/FolderTreeNode instances by id instead of clearing and rebuilding.
    /// Refresh runs after nearly every command, so a clear+rebuild here was destroying and
    /// recreating every node on every edit, which collapsed the sidebar's expand state (Avalonia
    /// tracks it by item identity) and, for the Enter-to-commit rename path, tore down the very
    /// TextBox that was still mid-dispatch of its own KeyDown event.</summary>
    private void RefreshTree()
    {
        SyncByKey(Tree, Collections,
            collection => collection.Id,
            node => node.Collection.Id,
            collection => new CollectionTreeNode(collection));

        foreach (var node in Tree)
        {
            node.RefreshActiveEnvironment();
            SyncFolderLevel(node.Requests, node.Folders, node.Children, node.Collection, parentFolderId: null);
        }
    }

    private static void SyncFolderLevel(
        ObservableCollection<RequestItem> requestsNode,
        ObservableCollection<FolderTreeNode> foldersNode,
        ObservableCollection<object> childrenNode,
        RequestCollection collection,
        Guid? parentFolderId)
    {
        // Requests are their own sidebar nodes, and a save replaces the RequestItem instance
        // under the same Id - so match on identity too, or the row keeps the pre-save object and
        // reopening it shows the old version.
        SyncByKey(requestsNode,
            collection.Requests.Where(request => request.FolderId == parentFolderId).OrderBy(request => request.SortOrder).ToList(),
            request => request.Id, request => request.Id, request => request,
            isCurrent: (node, request) => ReferenceEquals(node, request));

        SyncByKey(foldersNode,
            collection.Folders.Where(folder => folder.ParentFolderId == parentFolderId).OrderBy(folder => folder.SortOrder).ToList(),
            folder => folder.Id, node => node.Folder.Id, folder => new FolderTreeNode(folder));

        SyncChildren(childrenNode, [.. foldersNode, .. requestsNode]);

        foreach (var folderNode in foldersNode)
        {
            SyncFolderLevel(folderNode.Requests, folderNode.Folders, folderNode.Children, collection, folderNode.Folder.Id);
        }
    }

    /// <summary>Reconciles a node's combined child list in place by identity, so unchanged
    /// children keep their tree containers (and expand state).</summary>
    private static void SyncChildren(ObservableCollection<object> target, IReadOnlyList<object> desired)
    {
        for (var i = 0; i < desired.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], desired[i])) continue;
            var existing = -1;
            for (var j = i + 1; j < target.Count; j++)
            {
                if (ReferenceEquals(target[j], desired[i])) { existing = j; break; }
            }
            if (existing >= 0) target.Move(existing, i);
            else target.Insert(i, desired[i]);
        }

        while (target.Count > desired.Count) target.RemoveAt(target.Count - 1);
    }

    /// <summary>Reconciles <paramref name="target"/> to match <paramref name="desired"/>, keyed by
    /// id, reusing existing nodes (and their identity) wherever the key already exists rather than
    /// recreating them - the point being that unrelated nodes are left completely untouched.</summary>
    private static void SyncByKey<TModel, TNode>(
        ObservableCollection<TNode> target,
        IReadOnlyList<TModel> desired,
        Func<TModel, Guid> modelKey,
        Func<TNode, Guid> nodeKey,
        Func<TModel, TNode> createNode,
        Func<TNode, TModel, bool>? isCurrent = null)
    {
        var existingByKey = target.ToDictionary(nodeKey);

        for (var i = 0; i < desired.Count; i++)
        {
            var key = modelKey(desired[i]);
            if (i < target.Count && nodeKey(target[i]) == key)
            {
                if (isCurrent is not null && !isCurrent(target[i], desired[i]))
                {
                    target[i] = createNode(desired[i]);
                }

                continue;
            }

            if (existingByKey.TryGetValue(key, out var node))
            {
                target.Remove(node);
                target.Insert(i, isCurrent is null || isCurrent(node, desired[i]) ? node : createNode(desired[i]));
            }
            else
            {
                target.Insert(i, createNode(desired[i]));
            }
        }

        while (target.Count > desired.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }
}
