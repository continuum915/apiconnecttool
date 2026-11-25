using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.ViewModels;

/// <summary>View-only projection of a saved collection hierarchy. The persisted model remains
/// normalized around FolderId/ParentFolderId; this projection exists so the sidebar can render
/// that hierarchy without duplicating or mutating workspace data.</summary>
public sealed partial class CollectionTreeNode : ObservableObject
{
    public RequestCollection Collection { get; }
    public ObservableCollection<FolderTreeNode> Folders { get; } = new();
    public ObservableCollection<RequestItem> Requests { get; } = new();

    /// <summary>Folders then requests, the order the navigator tree shows them.</summary>
    public ObservableCollection<object> Children { get; } = new();

    /// <summary>The name of the collection's active environment as shown on its navigator row
    /// ("No environment" when none is active), or null when the collection has no environments.
    /// Production is marked separately by <see cref="IsActiveEnvironmentProduction"/>.</summary>
    [ObservableProperty] private string? _activeEnvironmentLabel;
    [ObservableProperty] private bool _isActiveEnvironmentProduction;

    public CollectionTreeNode(RequestCollection collection)
    {
        Collection = collection;
        RefreshActiveEnvironment();
    }

    /// <summary>Rereads the active environment, which may have been switched, renamed, deleted,
    /// or marked production since the last refresh.</summary>
    public void RefreshActiveEnvironment()
    {
        var active = Collection.ActiveEnvironment;
        ActiveEnvironmentLabel = active is not null ? active.Name
            : Collection.Environments.Count > 0 ? AppLocalization.Get("Str.Runner.NoCollectionEnvironment") : null;
        IsActiveEnvironmentProduction = active?.IsProduction == true;
    }
}

public sealed class FolderTreeNode
{
    public CollectionFolder Folder { get; }
    public ObservableCollection<FolderTreeNode> Folders { get; } = new();
    public ObservableCollection<RequestItem> Requests { get; } = new();

    /// <summary>Folders then requests, the order the navigator tree shows them.</summary>
    public ObservableCollection<object> Children { get; } = new();

    public FolderTreeNode(CollectionFolder folder) => Folder = folder;
}
