using System;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

public interface ICollectionExportService
{
    /// <summary>
    /// Serializes <paramref name="collection"/> (folders + requests) as a native ApiConnectTool export
    /// file. Secret-valued auth fields are blanked unless <paramref name="includeSecrets"/> is
    /// explicitly true, in which case they're written as plain text with no encryption - an
    /// explicit, user-driven choice, never a default.
    /// </summary>
    string Export(RequestCollection collection, Guid sourceWorkspaceId, bool includeSecrets);

    /// <summary>
    /// Parses a native ApiConnectTool export file into a standalone <see cref="RequestCollection"/> with
    /// fresh IDs throughout (collection, every folder, every request - FolderId/ParentFolderId
    /// references remapped to match), so importing a file back into its own source workspace can
    /// never collide with the requests it was exported from. Throws
    /// <see cref="System.IO.InvalidDataException"/> for a malformed file or one whose
    /// ExportSchemaVersion is newer than this build supports.
    /// </summary>
    RequestCollection Import(string json);
}
