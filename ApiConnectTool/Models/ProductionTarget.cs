using System.Collections.Generic;

namespace ApiConnectTool.Models;

/// <summary>A production-marked environment a send uses. <see cref="CollectionName"/> names the
/// owning collection for a collection environment and is null for the active global environment.</summary>
public sealed record ProductionTarget(RequestEnvironment Environment, string? CollectionName = null)
{
    /// <summary>The production environments among a send's collection environment and global
    /// environment, collection environment first. Empty when neither is marked production.</summary>
    public static IReadOnlyList<ProductionTarget> Collect(RequestEnvironment? globalEnvironment,
        RequestEnvironment? collectionEnvironment, string? collectionName)
    {
        var targets = new List<ProductionTarget>();
        if (collectionEnvironment?.IsProduction == true) targets.Add(new(collectionEnvironment, collectionName ?? string.Empty));
        if (globalEnvironment?.IsProduction == true) targets.Add(new(globalEnvironment));
        return targets;
    }

    /// <summary>A copy that no later rename or production-flag edit can change.</summary>
    public ProductionTarget Snapshot() =>
        new(new RequestEnvironment { Id = Environment.Id, Name = Environment.Name, IsProduction = Environment.IsProduction }, CollectionName);
}
