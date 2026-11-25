using System;

namespace ApiConnectTool.Services;

/// <summary>Thrown by <see cref="IOpenApiImporter"/> for a document that isn't a recognizable
/// OpenAPI spec - a distinct, catchable type so a caller can show a validation-style message
/// rather than a generic error.</summary>
public sealed class OpenApiImportException(string message, Exception? innerException = null)
    : Exception(message, innerException);
