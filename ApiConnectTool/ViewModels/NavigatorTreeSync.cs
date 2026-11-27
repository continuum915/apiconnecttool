using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ApiConnectTool.ViewModels;

/// <summary>Stateless helpers over the navigator's tree projection: reconciling node lists in
/// place (so unchanged nodes keep their identity, and with it their tree containers and expand
/// state) and finding the path to the node that shows a given model object.</summary>
internal static class NavigatorTreeSync
{
    /// <summary>The tree nodes from a collection node down to the node showing
    /// <paramref name="model"/> (a RequestCollection, CollectionFolder or RequestItem), or null.</summary>
    public static IReadOnlyList<object>? PathTo(IEnumerable<CollectionTreeNode> tree, object model)
    {
        foreach (var collectionNode in tree)
        {
            if (ReferenceEquals(collectionNode.Collection, model)) return [collectionNode];
            if (FindInLevel(collectionNode.Children, model) is { } rest) return [collectionNode, .. rest];
        }

        return null;
    }

    private static List<object>? FindInLevel(IEnumerable<object> children, object model)
    {
        foreach (var child in children)
        {
            if (ReferenceEquals(child, model) || child is FolderTreeNode found && ReferenceEquals(found.Folder, model)) return [child];
            if (child is FolderTreeNode folderNode && FindInLevel(folderNode.Children, model) is { } rest) return [folderNode, .. rest];
        }

        return null;
    }

    /// <summary>Reconciles a node's combined child list in place by identity, so unchanged
    /// children keep their tree containers (and expand state).</summary>
    public static void SyncChildren(ObservableCollection<object> target, IReadOnlyList<object> desired)
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
    public static void SyncByKey<TModel, TNode>(
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
