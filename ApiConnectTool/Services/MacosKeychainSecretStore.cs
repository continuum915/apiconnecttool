using System;
using System.Runtime.Versioning;

namespace ApiConnectTool.Services;

/// <summary>
/// Protects secrets in the user's login Keychain via the `security` CLI (shipped with macOS;
/// no extra dependency). Each protected value gets its own randomly generated account name
/// stored under a fixed service name; the payload persisted in the workspace is that account
/// name, not the secret itself, so the actual value never leaves the Keychain.
/// Known limitation: the plaintext value is passed as a `security` command-line argument for
/// the store operation, which is briefly visible to other processes on the same host via the
/// process table (e.g. `ps`) — the classic macOS `security` tool has no stdin-based store
/// path. This is an accepted tradeoff over the alternative of a much larger, unverifiable
/// CoreFoundation/Security.framework P/Invoke surface.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacosKeychainSecretStore : ISecretStore
{
    private const string ServiceName = "ApiConnectTool";
    private readonly IProcessRunner _processRunner;

    public MacosKeychainSecretStore() : this(new SystemProcessRunner())
    {
    }

    public MacosKeychainSecretStore(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public bool TryProtect(string plaintext, out string? protectedPayload)
    {
        var account = Guid.NewGuid().ToString("N");
        var result = _processRunner.Run("security", new[]
        {
            "add-generic-password",
            "-a", account,
            "-s", ServiceName,
            "-w", plaintext,
            "-U",
        });

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
        var result = _processRunner.Run("security", new[]
        {
            "find-generic-password",
            "-a", protectedPayload,
            "-s", ServiceName,
            "-w",
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
