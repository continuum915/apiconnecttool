using System;
using System.Runtime.Versioning;

namespace ApiConnectTool.Services;

/// <summary>
/// Protects secrets via the freedesktop.org Secret Service (GNOME Keyring, KWallet, etc.)
/// using the `secret-tool` CLI from libsecret. As with the macOS store, the payload persisted
/// in the workspace is a generated per-secret account identifier, not the secret itself. Unlike
/// the macOS `security` tool, `secret-tool store` reads the value from stdin, so the plaintext
/// is never passed as a process argument.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxSecretServiceStore : ISecretStore
{
    private const string ServiceAttributeValue = "apiconnecttool";
    private readonly IProcessRunner _processRunner;

    public LinuxSecretServiceStore() : this(new SystemProcessRunner())
    {
    }

    public LinuxSecretServiceStore(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public bool TryProtect(string plaintext, out string? protectedPayload)
    {
        var account = Guid.NewGuid().ToString("N");
        var result = _processRunner.Run("secret-tool", new[]
        {
            "store",
            "--label=ApiConnectTool secret",
            "service", ServiceAttributeValue,
            "account", account,
        }, standardInput: plaintext);

        if (result.ExitCode != 0)
        {
            protectedPayload = null;
            return false;
        }

        protectedPayload = account;
        return true;
    }

    public bool TryUnprotect(string protectedPayload, out string? plaintext)
    {
        var result = _processRunner.Run("secret-tool", new[]
        {
            "lookup",
            "service", ServiceAttributeValue,
            "account", protectedPayload,
        });

        if (result.ExitCode != 0)
        {
            plaintext = null;
            return false;
        }

        plaintext = result.StandardOutput.TrimEnd('\r', '\n');
        return true;
    }
}
