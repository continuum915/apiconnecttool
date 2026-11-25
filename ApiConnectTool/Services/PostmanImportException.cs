using System;

namespace ApiConnectTool.Services;

/// <summary>Thrown by <see cref="IPostmanCollectionConverter"/> for a document that isn't a
/// recognizable Postman collection - a distinct, catchable type so a caller can show a
/// validation-style message rather than a generic error.</summary>
public sealed class PostmanImportException(string message, Exception? innerException = null)
    : Exception(message, innerException);
