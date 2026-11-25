using System;
using System.Collections.Generic;

namespace ApiConnectTool.Models;

public sealed class CollectionRunConfiguration
{
    /// <summary>The global environment for this run.</summary>
    public RequestEnvironment? Environment { get; init; }

    private readonly RequestEnvironment? _collectionEnvironment;
    /// <summary>The collection environment for this run, overriding the collection's active one without
    /// changing it. Null when set means "No environment"; leave unset to use the active selection.</summary>
    public RequestEnvironment? CollectionEnvironment
    {
        get => _collectionEnvironment;
        init { _collectionEnvironment = value; HasCollectionEnvironment = true; }
    }
    public bool HasCollectionEnvironment { get; private init; }
    public Guid? FolderId { get; init; }
    public IReadOnlyList<IReadOnlyDictionary<string, string>>? IterationRows { get; init; }
    public TimeSpan DelayBetweenRequests { get; init; }
    public bool StopOnFailure { get; init; }
    public int ScriptTimeoutMilliseconds { get; init; } = 2000;
    public IReadOnlySet<Guid>? IncludedRequestIds { get; init; }
}
