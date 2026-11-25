using System.Collections.Generic;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

public interface IWorkspaceService
{
    Workspace LoadWorkspace();
    void SaveWorkspace(Workspace workspace);
    string? LastLoadError { get; }

    /// <summary>The schema version of a workspace written by a newer build, or null when the
    /// workspace is supported. While set, every save is refused and the files stay untouched.</summary>
    int? UnsupportedSchemaVersion { get; }

    /// <summary>Secrets the last load found but could not decrypt. Their stored payloads are
    /// written back unchanged by every save until a new value is assigned.</summary>
    IReadOnlyList<UndecryptableSecretLocation> UndecryptableSecrets { get; }
}
