using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace ApiConnectTool.Services;

internal static class PersistenceFailures
{
    public static bool Try(Action action)
    {
        try { action(); return true; }
        catch (Exception exception) when (IsExpected(exception)) { return false; }
    }

    public static bool IsExpected(Exception exception) => exception is IOException or UnauthorizedAccessException
        or CryptographicException or SecretProtectionException or JsonException;
}
