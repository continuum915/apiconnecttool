using System.IO;

namespace ApiConnectTool.Services;

/// <summary>Thrown when a save is attempted over a workspace written by a newer build. It is an
/// <see cref="IOException"/> so tolerant callers that already survive a failed write keep working.</summary>
public sealed class UnsupportedWorkspaceVersionException(int schemaVersion)
    : IOException($"The workspace uses schema version {schemaVersion}, which this build does not support. It was left unchanged.")
{
    public int SchemaVersion { get; } = schemaVersion;
}
