using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

public interface IPostmanCollectionConverter
{
    /// <summary>
    /// Serializes <paramref name="collection"/> as a Postman Collection v2.1 document. Secret
    /// auth values (Bearer token, Basic password, API key value) are blanked unless
    /// <paramref name="includeSecrets"/> is explicitly true, matching the native export format's
    /// (4g1) redaction-by-default posture - never a default that leaks secrets into an exported
    /// file.
    /// </summary>
    string Export(RequestCollection collection, bool includeSecrets);

    /// <summary>
    /// Parses a Postman Collection v2.1 document into a standalone <see cref="RequestCollection"/>
    /// with fresh IDs throughout. Postman's <c>{{variable}}</c> syntax is the same delimiter ApiConnectTool
    /// uses, so variable references transfer unchanged. Throws
    /// <see cref="PostmanImportException"/> for a document that isn't a recognizable collection.
    /// </summary>
    RequestCollection Import(string json);
    PostmanImportResult ImportWithReport(string json);
}
