namespace ApiConnectTool.Services;

/// <summary>
/// Platform-abstracted secret protection. Implementations must fail closed: a store that
/// cannot guarantee protection returns false rather than falling back to plaintext.
/// </summary>
public interface ISecretStore
{
    bool TryProtect(string plaintext, out string? protectedPayload);
    bool TryUnprotect(string protectedPayload, out string? plaintext);
}
