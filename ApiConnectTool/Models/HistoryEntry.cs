using System;

namespace ApiConnectTool.Models;

public class HistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // null if the tab that sent this was never saved to a collection.
    public Guid? SourceRequestId { get; set; }
    public DateTimeOffset SentAtUtc { get; set; }

    // Pre-resolution template ({{variable}} tokens intact) - inherently secret-safe, and exactly
    // what "resend from history" (4d) needs to re-resolve against whichever environment is
    // selected at resend time.
    public RequestItem RequestSnapshot { get; set; } = new();

    // RequestSnapshot.Body is capped by HistoryRequestBody; true when the stored body is only a
    // prefix of what was sent. Absent in history files written before the cap, so it defaults false.
    public bool RequestBodyWasTruncatedForHistory { get; set; }

    public int? StatusCode { get; set; }
    public string? ReasonPhrase { get; set; }

    // Redacted via ISecretRedactor before assignment - never store the raw response text and
    // redact on read.
    public string RedactedResponseHeadersText { get; set; } = string.Empty;
    public string RedactedResponseBodyPreview { get; set; } = string.Empty;

    // Capped independently of RequestExecutionOptions.MaxResponseBytes so the history file stays
    // bounded regardless of how large an individual response was allowed to be for display.
    public bool ResponseBodyWasTruncatedForHistory { get; set; }

    public TimeSpan Elapsed { get; set; }

    // Response body size in bytes as received (Content-Length when given). Absent in history files
    // written before the History window, so it is optional.
    public long? ResponseSize { get; set; }
    public bool IsSuccess { get; set; }

    // Already redacted before assignment.
    public string? ErrorMessage { get; set; }
}
