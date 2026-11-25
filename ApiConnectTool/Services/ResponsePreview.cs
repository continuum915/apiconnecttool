using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

/// <summary>A redacted, size-capped text view of a response (and of the request that produced
/// it), as kept by history and collection run results. Secrets are redacted before the body is
/// capped, so a secret straddling the cut cannot leave a readable prefix behind.</summary>
public sealed record ResponsePreview(string HeadersText, string BodyPreview, bool BodyWasTruncated)
{
    /// <summary>Most response-body bytes a preview keeps, the same bound history uses.</summary>
    public const int MaxBodyPreviewBytes = 16 * 1024;

    private static readonly SecretRedactor Redactor = new();

    public static ResponsePreview Create(RequestExecutionResult result, IEnumerable<string> secrets, int maxBodyBytes = MaxBodyPreviewBytes)
    {
        var known = secrets as IReadOnlyCollection<string> ?? secrets.ToArray();
        var truncated = result.Body.Length > maxBodyBytes;
        var previewBytes = truncated ? result.Body[..maxBodyBytes] : result.Body;
        var body = result.Body.Length == 0
            ? string.Empty
            : IsText(result.ContentType)
                ? Decode(previewBytes, result.CharacterSet)
                : $"[Binary response, {result.Body.LongLength} bytes]";
        return new(Redactor.Redact(FormatHeaders(result.Headers), known), Redactor.Redact(body, known), truncated);
    }

    public static string FormatHeaders(IEnumerable<KeyValueItem> headers) =>
        string.Join("\n", headers.Select(header => $"{header.Key}: {header.Value}"));

    /// <summary>The request line and headers actually sent, redacted.</summary>
    public static (string Url, string HeadersText) Request(SentRequest? sent, IEnumerable<string> secrets)
    {
        if (sent is null) return (string.Empty, string.Empty);
        var known = secrets as IReadOnlyCollection<string> ?? secrets.ToArray();
        return (Redactor.Redact(sent.Url.ToString(), known), Redactor.Redact(FormatHeaders(sent.Headers), known));
    }

    public static bool IsText(string contentType) =>
        contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
        contentType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
        contentType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
        contentType.Contains("javascript", StringComparison.OrdinalIgnoreCase) ||
        contentType.Contains("yaml", StringComparison.OrdinalIgnoreCase);

    private static string Decode(byte[] bytes, string? characterSet)
    {
        try { return Encoding.GetEncoding(string.IsNullOrWhiteSpace(characterSet) ? "utf-8" : characterSet).GetString(bytes); }
        catch (ArgumentException) { return Encoding.UTF8.GetString(bytes); }
    }
}
