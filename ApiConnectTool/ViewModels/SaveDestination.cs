using System;
using ApiConnectTool.Models;

namespace ApiConnectTool.ViewModels;

/// <summary>A place a request can live: a collection's root (<paramref name="FolderId"/> null) or
/// one of its folders. Pickers show <paramref name="Label"/> (the collection or folder's own
/// name) indented by <paramref name="Depth"/>, so a folder is never hidden behind a long
/// collection name; <paramref name="DisplayPath"/> is the full "Collection / Folder / Sub" path
/// (tooltip, identification in tests).</summary>
public sealed record SaveDestination(RequestCollection Collection, Guid? FolderId, string DisplayPath, string Label, int Depth)
{
    public bool IsCollection => FolderId is null;

    public double IndentWidth => Depth * 20;

    public bool IsSameLocation(SaveDestination? other) =>
        other is not null && ReferenceEquals(Collection, other.Collection) && FolderId == other.FolderId;

    public override string ToString() => DisplayPath;
}
