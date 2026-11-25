using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

public interface IOpenApiImporter
{
    /// <summary>
    /// Parses an OpenAPI 3.x document into a <see cref="RequestCollection"/> - one
    /// <see cref="RequestItem"/> per path/operation. Operation tags become first-level folders.
    /// Path parameters (<c>{param}</c>) are
    /// rewritten to ApiConnectTool's <c>{{param}}</c> variable syntax; a JSON request body is generated
    /// best-effort from the operation's schema, degrading to an empty placeholder for anything it
    /// can't confidently resolve ($ref cycles, oneOf/anyOf) rather than failing the whole import.
    /// Import only; OpenAPI export is out of scope.
    /// </summary>
    RequestCollection Import(string json);

    /// <summary>Imports a document and returns security mapping diagnostics alongside the collection.</summary>
    OpenApiImportResult ImportWithReport(string json);
}
