using System;
using System.Collections.Generic;

namespace ApiConnectTool.Models;

public sealed class RequestExecutionOptions
{
    public const int DefaultMaxResponseBytes = 5 * 1024 * 1024;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public TimeSpan Timeout { get; init; } = DefaultTimeout;
    public int MaxResponseBytes { get; init; } = DefaultMaxResponseBytes;
    public bool FollowRedirects { get; init; } = true;
    public string? ProxyUrl { get; init; }
    /// <summary>The active global environment; binds OAuth tokens together with <see cref="CollectionEnvironmentId"/>.</summary>
    public Guid? EnvironmentId { get; init; }
    /// <summary>The active environment of the request's collection, or null for none.</summary>
    public Guid? CollectionEnvironmentId { get; init; }

    /// <summary>
    /// {{name}} -> value map resolved from the currently selected environment. Applied to the
    /// URL, query params, headers, auth fields, and body before the request is sent.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Variables { get; init; }

    /// <summary>
    /// Names (a subset of <see cref="Variables"/>' keys) whose value is secret-flagged in the
    /// source environment. Used to populate <see cref="RequestExecutionResult.ResolvedSecretValues"/>
    /// with exactly the secret values actually substituted for this send, so redaction
    /// doesn't have to guess which resolved values are sensitive.
    /// </summary>
    public IReadOnlySet<string>? SecretVariableNames { get; init; }

    /// <summary>
    /// Skips TLS certificate validation. Dangerous: only ever set this from an explicit,
    /// per-request, visible opt-in. The saved request retains this setting.
    /// </summary>
    public bool IgnoreCertificateErrors { get; init; }
    public ApiConnectTool.Services.ScriptVariableScopes? ScriptScopes { get; init; }
    public IReadOnlyDictionary<string, string>? RunVariables { get; init; }
}
