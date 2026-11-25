using System;
using System.Collections.Generic;
using System.Linq;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

internal static class WorkspaceMigrator
{
    /// <param name="clearedSelections">When given, receives the name of each collection whose
    /// active environment pointed at an environment it does not own and was cleared.</param>
    public static bool Normalize(Workspace workspace, List<string>? clearedSelections = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var changed = false;

        // Captured before the version bump below: pre-3 requests have no meaningful SortOrder
        // (defaults to 0), so their existing list position is the order to preserve.
        var wasPreFolderSchema = workspace.SchemaVersion < 3;
        var wasPreInheritanceSchema = workspace.SchemaVersion < 7;

        if (workspace.Id == Guid.Empty)
        {
            workspace.Id = Guid.NewGuid();
            changed = true;
        }

        if (workspace.SchemaVersion < Workspace.CurrentSchemaVersion)
        {
            workspace.SchemaVersion = Workspace.CurrentSchemaVersion;
            changed = true;
        }

        workspace.Collections ??= new();
        workspace.GlobalVariables ??= new();
        foreach (var collection in workspace.Collections)
        {
            collection.Variables ??= new();
            if (collection.Id == Guid.Empty)
            {
                collection.Id = Guid.NewGuid();
                changed = true;
            }

            collection.Folders ??= new();
            foreach (var folder in collection.Folders)
            {
                if (folder.Id == Guid.Empty)
                {
                    folder.Id = Guid.NewGuid();
                    changed = true;
                }
            }

            collection.Requests ??= new();
            for (var i = 0; i < collection.Requests.Count; i++)
            {
                var request = collection.Requests[i];
                if (request.Id == Guid.Empty)
                {
                    request.Id = Guid.NewGuid();
                    changed = true;
                }

                if (wasPreFolderSchema)
                {
                    request.SortOrder = i;
                    changed = true;
                }

                if (request.Assertions is null)
                {
                    request.Assertions = new();
                    changed = true;
                }
                request.QueryParams ??= new();
                changed |= RequestQueryString.Normalize(request);
                request.Headers ??= new();
                request.FormData ??= new();
                request.Auth ??= new();
                if (wasPreInheritanceSchema && request.Auth.Type == AuthType.Inherit)
                    request.Auth.Type = AuthType.None;

#pragma warning disable CS0618
                // An unreadable legacy token moves too, so its preserved payload lives where
                // request editing and saving carry it rather than in a field they drop.
                if (request.Auth.Type == AuthType.None && (!string.IsNullOrWhiteSpace(request.AuthToken.Value) || request.AuthToken.FailedToDecrypt))
                {
                    request.Auth.Type = AuthType.Bearer;
                    request.Auth.Token = request.AuthToken;
                    request.AuthToken = new EncryptedString(string.Empty);
                    changed = true;
                }
#pragma warning restore CS0618
            }
        }

        // Global environments are visited first so they keep their IDs; a collection environment
        // that collides with any earlier environment gets a fresh one.
        var environmentIds = new HashSet<Guid>();
        workspace.Environments ??= new();
        foreach (var environment in workspace.Environments)
            changed |= NormalizeEnvironment(environment, environmentIds);

        foreach (var collection in workspace.Collections)
        {
            collection.Environments ??= new();
            foreach (var environment in collection.Environments)
            {
                var originalId = environment.Id;
                if (!NormalizeEnvironment(environment, environmentIds)) continue;
                changed = true;
                if (collection.ActiveEnvironmentId == originalId && collection.Environments.All(other => other.Id != originalId))
                    collection.ActiveEnvironmentId = environment.Id;
            }

            if (collection.ActiveEnvironmentId is { } activeId && collection.Environments.All(environment => environment.Id != activeId))
            {
                collection.ActiveEnvironmentId = null;
                clearedSelections?.Add(collection.Name);
                changed = true;
            }
        }

        if (workspace.SelectedEnvironmentId is { } selectedId &&
            workspace.Environments.All(environment => environment.Id != selectedId))
        {
            workspace.SelectedEnvironmentId = null;
            changed = true;
        }

        return changed;
    }

    /// <summary>Gives the environment and its variables usable IDs. Returns true when anything changed.</summary>
    private static bool NormalizeEnvironment(RequestEnvironment environment, HashSet<Guid> seenIds)
    {
        var changed = false;
        if (environment.Id == Guid.Empty || !seenIds.Add(environment.Id))
        {
            environment.Id = Guid.NewGuid();
            seenIds.Add(environment.Id);
            changed = true;
        }

        environment.Variables ??= new();
        foreach (var variable in environment.Variables)
        {
            if (variable.Id == Guid.Empty)
            {
                variable.Id = Guid.NewGuid();
                changed = true;
            }
        }

        return changed;
    }
}
