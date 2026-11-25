using System;
using System.Collections.Generic;
using System.Net;

namespace ApiConnectTool.Models;

public enum RequestExecutionFailureKind
{
    None,
    Validation,
    Timeout,
    Canceled,
    Network,
    Tls,
    Dns,
    File,
    Unexpected
}

public sealed class RequestExecutionResult
{
    public bool IsSuccess { get; init; }
    public RequestExecutionFailureKind FailureKind { get; init; }
    public string? ErrorMessage { get; init; }
    public HttpStatusCode? StatusCode { get; init; }
    public string? ReasonPhrase { get; init; }
    public long? ContentLength { get; init; }
    public bool BodyWasTruncated { get; init; }
    public TimeSpan Elapsed { get; init; }
    public string ContentType { get; init; } = "text/plain";
    public string? CharacterSet { get; init; }
    public byte[] Body { get; init; } = [];
    public IReadOnlyList<KeyValueItem> Headers { get; init; } = [];
    public Uri? FinalUri { get; init; }
    public IReadOnlyList<RedirectHop> RedirectHistory { get; init; } = [];

    /// <summary>
    /// The exact set of secret literal values (auth token/password/API key/client secret, OAuth
    /// access token, and any secret-flagged environment variable) that were substituted into this
    /// send. Populated but never itself serialized or logged - callers (history, cURL
    /// generation) use it to redact before anything touches disk or the clipboard.
    /// </summary>
    public IReadOnlyList<string> ResolvedSecretValues { get; init; } = [];

    /// <summary>The first request actually sent (method, resolved URL and headers), raw. Null when
    /// nothing was sent. Redact with <see cref="ResolvedSecretValues"/> before showing it.</summary>
    public SentRequest? SentRequest { get; set; }

    public static RequestExecutionResult Failed(RequestExecutionFailureKind kind, string message, TimeSpan elapsed = default, IReadOnlyList<string>? resolvedSecretValues = null) => new()
    {
        FailureKind = kind,
        ErrorMessage = message,
        Elapsed = elapsed,
        ResolvedSecretValues = resolvedSecretValues ?? []
    };
}

/// <summary>The method, URL and headers of an outgoing request, as sent.</summary>
public sealed record SentRequest(string Method, Uri Url, IReadOnlyList<KeyValueItem> Headers);
