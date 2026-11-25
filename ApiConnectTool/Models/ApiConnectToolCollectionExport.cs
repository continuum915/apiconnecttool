using System;

namespace ApiConnectTool.Models;

/// <summary>
/// The native ApiConnectTool export file envelope - a versioned wrapper around one <see cref="RequestCollection"/>.
/// <see cref="ExportSchemaVersion"/> is independent of <see cref="Workspace.CurrentSchemaVersion"/>:
/// it only bumps when this file format's own shape changes, not when the in-app workspace schema does.
/// </summary>
public class ApiConnectToolCollectionExport
{
    public int ExportSchemaVersion { get; set; }
    public DateTimeOffset ExportedAtUtc { get; set; }

    // Informational only - never re-imported as-is (import always mints a fresh workspace-local Id
    // for the collection itself).
    public Guid SourceWorkspaceId { get; set; }

    public bool IncludesSecrets { get; set; }
    public RequestCollection Collection { get; set; } = new();
}
