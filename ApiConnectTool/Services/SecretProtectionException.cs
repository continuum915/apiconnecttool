using System;

namespace ApiConnectTool.Services;

/// <summary>
/// Thrown when a secret value cannot be protected for storage. Callers must surface this
/// visibly and must never respond by writing the plaintext value instead.
/// </summary>
public sealed class SecretProtectionException(string message) : Exception(message);
