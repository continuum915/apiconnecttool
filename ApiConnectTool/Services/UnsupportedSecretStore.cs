namespace ApiConnectTool.Services;

/// <summary>
/// Explicit fail-closed store for platforms without a real secret-store implementation yet.
/// Never silently accepts a "protected" payload it cannot actually protect.
/// </summary>
public sealed class UnsupportedSecretStore : ISecretStore
{
    public bool TryProtect(string plaintext, out string? protectedPayload)
    {
        protectedPayload = null;
        return false;
    }

    public bool TryUnprotect(string protectedPayload, out string? plaintext)
    {
        plaintext = null;
        return false;
    }
}
