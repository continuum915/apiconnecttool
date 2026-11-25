#pragma warning disable CA1416
using System;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace ApiConnectTool.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsDpapiSecretStore : ISecretStore
{
    public bool TryProtect(string plaintext, out string? protectedPayload)
    {
        try
        {
            var plainBytes = Encoding.UTF8.GetBytes(plaintext);
            var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            protectedPayload = Convert.ToBase64String(encryptedBytes);
            return true;
        }
        catch (CryptographicException)
        {
            protectedPayload = null;
            return false;
        }
    }

    public bool TryUnprotect(string protectedPayload, out string? plaintext)
    {
        try
        {
            var encryptedBytes = Convert.FromBase64String(protectedPayload);
            var decryptedBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            plaintext = Encoding.UTF8.GetString(decryptedBytes);
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            plaintext = null;
            return false;
        }
    }
}
