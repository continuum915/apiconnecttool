using System.Linq;
using ApiConnectTool.Models;

namespace ApiConnectTool.ViewModels;

/// <summary>
/// A response, reduced to what the comparison view needs: a label, a status line, and the two
/// texts that get diffed. Built either from a captured <see cref="HistoryEntry"/> (already
/// redacted) or from a tab's current live response.
/// </summary>
public sealed record ResponseSnapshot(string Label, string StatusText, string HeadersText, string BodyText)
{
    public static ResponseSnapshot FromHistoryEntry(HistoryEntry entry) => new(
        $"{entry.RequestSnapshot.Name} — {entry.SentAtUtc.ToLocalTime():g}",
        entry.IsSuccess ? $"{entry.StatusCode} {entry.ReasonPhrase}".Trim() : entry.ErrorMessage ?? "Failed",
        entry.RedactedResponseHeadersText,
        entry.RedactedResponseBodyPreview);

    public static ResponseSnapshot FromCurrentTab(RequestTabViewModel tab) => new(
        $"{tab.Name} — current response",
        tab.ResponseStatus,
        string.Join("\n", tab.ResponseHeaders.Select(h => $"{h.Key}: {h.Value}")),
        tab.ResponseBody);
}
