using ApiConnectTool.Models;
using ApiConnectTool.Services;

namespace ApiConnectTool.Tests.ViewModels
{
    /// <summary>Minimal in-memory fake used by ViewModel tests.</summary>
    internal class FakeWorkspaceService : IWorkspaceService
    {
        public Workspace WorkspaceToReturn { get; set; } = new Workspace();
        public string? LastLoadError { get; set; }
        public int? UnsupportedSchemaVersion { get; set; }
        public System.Collections.Generic.IReadOnlyList<UndecryptableSecretLocation> UndecryptableSecrets { get; set; } = [];
        public Workspace? LastSaved { get; private set; }
        public int SaveCallCount { get; private set; }

        /// <summary>When set, SaveWorkspace throws this instead of recording the save.</summary>
        public System.Exception? SaveException { get; set; }

        public Workspace LoadWorkspace() => WorkspaceToReturn;

        public void SaveWorkspace(Workspace workspace)
        {
            if (UnsupportedSchemaVersion is { } version)
            {
                throw new UnsupportedWorkspaceVersionException(version);
            }

            if (SaveException is not null)
            {
                throw SaveException;
            }

            LastSaved = workspace;
            SaveCallCount++;
        }
    }
}
