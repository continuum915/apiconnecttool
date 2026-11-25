using System;

namespace ApiConnectTool.Services;

/// <summary>Thrown by <see cref="ICurlImporter"/> for malformed or unsupported input - a distinct,
/// catchable type so a caller can show a validation-style message rather than a generic error.</summary>
public sealed class CurlImportException(string message) : Exception(message);
