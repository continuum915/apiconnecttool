using System;
using System.Text;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Services;

/// <summary>
/// Bounds the raw request body kept in a history snapshot so one large upload can't bloat the
/// history file. The cut lands on a character boundary, never inside a UTF-8 sequence or a
/// surrogate pair. Form-data rows and file references are not touched.
/// </summary>
public static class HistoryRequestBody
{
    public const int MaxBytes = 64 * 1024;

    public static string IncompleteNotice =>
        string.Format(AppLocalization.Get("Str.History.RequestBodyTruncated"), MaxBytes / 1024);

    public static (string Body, bool WasTruncated) Cap(string body)
    {
        // Every UTF-16 char encodes to at most 3 UTF-8 bytes, so short bodies skip the count.
        if (body.Length <= MaxBytes / 3 || Encoding.UTF8.GetByteCount(body) <= MaxBytes) return (body, false);

        var span = body.AsSpan();
        var bytes = 0;
        var index = 0;
        while (index < span.Length)
        {
            Rune.DecodeFromUtf16(span[index..], out var rune, out var charsConsumed);
            // An unpaired surrogate decodes to U+FFFD, matching the 3 bytes GetByteCount charges it.
            if (bytes + rune.Utf8SequenceLength > MaxBytes) break;
            bytes += rune.Utf8SequenceLength;
            index += charsConsumed;
        }

        return (body[..index], true);
    }
}
