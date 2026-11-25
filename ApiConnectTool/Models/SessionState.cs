using System;
using System.Collections.Generic;
using System.Linq;

namespace ApiConnectTool.Models;

public sealed record SessionState(IReadOnlyList<Guid> OpenRequestIds, Guid? ActiveRequestId)
{
    public static SessionState Empty { get; } = new([], null);

    /// <summary>Whether the Variables pane is shown. Missing in older session files, so it
    /// defaults to shown.</summary>
    public bool VariablesPaneVisible { get; init; } = true;

    public static SessionState Capture(
        IEnumerable<Guid?> openTabRequestIds,
        Guid? activeTabRequestId,
        IEnumerable<RequestCollection> collections)
    {
        var savedIds = collections
            .SelectMany(collection => collection.Requests)
            .Select(request => request.Id)
            .ToHashSet();
        var openIds = openTabRequestIds
            .Where(id => id is { } value && savedIds.Contains(value))
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        Guid? activeId = activeTabRequestId is { } currentId && savedIds.Contains(currentId) ? currentId : null;
        return new SessionState(openIds, activeId);
    }

    public IReadOnlyList<RequestItem> ResolveRequests(IEnumerable<RequestCollection> collections)
    {
        var requestsById = collections
            .SelectMany(collection => collection.Requests)
            .DistinctBy(request => request.Id)
            .ToDictionary(request => request.Id);
        return OpenRequestIds
            .Where(requestsById.ContainsKey)
            .Distinct()
            .Select(id => requestsById[id])
            .ToArray();
    }
}
