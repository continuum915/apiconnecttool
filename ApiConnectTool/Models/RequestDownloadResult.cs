using System;
using System.Collections.Generic;
using System.Net;

namespace ApiConnectTool.Models;

public sealed class RequestDownloadResult
{
    public bool IsSuccess { get; init; }
    public RequestExecutionFailureKind FailureKind { get; init; }
    public string? ErrorMessage { get; init; }
    public HttpStatusCode? StatusCode { get; init; }
    public string? ReasonPhrase { get; init; }
    public long BytesWritten { get; init; }
    public TimeSpan Elapsed { get; init; }
    public Uri? FinalUri { get; init; }
    public IReadOnlyList<RedirectHop> RedirectHistory { get; init; } = [];

    /// <summary>
    /// See <see cref="RequestExecutionResult.ResolvedSecretValues"/> - same contract, for the
    /// download path.
    /// </summary>
    public IReadOnlyList<string> ResolvedSecretValues { get; init; } = [];

    public static RequestDownloadResult Failed(RequestExecutionFailureKind kind, string message, TimeSpan elapsed = default, IReadOnlyList<string>? resolvedSecretValues = null) => new()
    {
        FailureKind = kind,
        ErrorMessage = message,
        Elapsed = elapsed,
        ResolvedSecretValues = resolvedSecretValues ?? []
    };
}
