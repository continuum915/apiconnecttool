using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Services;

public enum ConsoleEntryKind { Info, Network, Script, Warning, Error }

/// <summary>The expandable part of a network entry, already redacted.</summary>
public sealed record ConsoleNetworkDetails(string Method, string Url, int? StatusCode, TimeSpan Elapsed,
    string RequestHeaders, string ResponseHeaders, string BodyPreview, bool BodyTruncated);

/// <summary>One Console line. Text is redacted before it is stored.</summary>
public sealed record ConsoleEntry(ConsoleEntryKind Kind, DateTimeOffset TimestampUtc, string Text, ConsoleNetworkDetails? Network = null);

/// <summary>The session Console: what the app did across sends and runs. In memory only, never
/// persisted or exported, and bounded to <see cref="MaxEntries"/> with the oldest dropped.</summary>
public interface IConsoleLog
{
    /// <summary>When false, the log clears when a collection run starts.</summary>
    bool PreserveLog { get; set; }

    /// <summary>Raised, possibly on a background thread, after entries were added or cleared.</summary>
    event EventHandler? Changed;

    IReadOnlyList<ConsoleEntry> Snapshot();
    void Add(ConsoleEntry entry);
    void Clear();
}

public sealed class ConsoleLog : IConsoleLog
{
    public const int MaxEntries = 2000;

    private readonly object _lock = new();
    private readonly LinkedList<ConsoleEntry> _entries = new();

    public bool PreserveLog { get; set; }

    public event EventHandler? Changed;

    public IReadOnlyList<ConsoleEntry> Snapshot()
    {
        lock (_lock) return _entries.ToArray();
    }

    public void Add(ConsoleEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_lock)
        {
            _entries.AddLast(entry);
            while (_entries.Count > MaxEntries) _entries.RemoveFirst();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_lock) _entries.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Turns sends and runs into Console entries. Everything is redacted with the send's
/// final secret set before it reaches the log; raw values are never stored.</summary>
public static partial class ConsoleRecorder
{
    /// <summary>Most response-body bytes a network entry keeps.</summary>
    public const int MaxBodyPreviewBytes = 4 * 1024;

    [GeneratedRegex(@"\{\{\s*([^{}]+?)\s*\}\}")]
    private static partial Regex Token();

    public static void RecordRunStarted(IConsoleLog log, string collectionName)
    {
        if (!log.PreserveLog) log.Clear();
        log.Add(new(ConsoleEntryKind.Info, DateTimeOffset.UtcNow, Format("Str.Console.RunStarted", collectionName)));
    }

    public static void RecordRunFinished(IConsoleLog log, string collectionName, CollectionRunReport report) =>
        log.Add(new(ConsoleEntryKind.Info, DateTimeOffset.UtcNow, string.Format(CultureInfo.CurrentCulture,
            DefaultStrings.Get("Str.Console.RunFinished"), collectionName, report.RunCount, report.PassedCount,
            report.FailedCount, report.ErroredCount, report.TotalElapsed.TotalMilliseconds)));

    /// <summary>Records one send: its network exchange, script console output, warnings and errors.</summary>
    public static void RecordSend(IConsoleLog log, string requestName, RequestRunResult run, IEnumerable<string> secrets)
    {
        var known = secrets.Where(secret => !string.IsNullOrEmpty(secret)).Distinct().ToArray();
        var redactor = new SecretRedactor();
        string Redact(string text) => redactor.Redact(text, known);
        var name = Redact(requestName);
        var execution = run.Execution;
        var now = DateTimeOffset.UtcNow;

        foreach (var line in run.Console)
        {
            var phase = DefaultStrings.Get(line.Phase == ScriptPhase.PreRequest ? "Str.Console.PreRequest" : "Str.Console.Test");
            log.Add(new(ConsoleEntryKind.Script, now, string.Format(CultureInfo.CurrentCulture,
                DefaultStrings.Get("Str.Console.ScriptLine"), name, phase, line.LevelText, Redact(line.Text))));
        }

        if (execution.SentRequest is { } sent)
        {
            var preview = ResponsePreview.Create(execution, known, MaxBodyPreviewBytes);
            var (url, requestHeaders) = ResponsePreview.Request(sent, known);
            var status = (int?)execution.StatusCode;
            log.Add(new(ConsoleEntryKind.Network, now, $"{sent.Method} {url}",
                new ConsoleNetworkDetails(sent.Method, url, status, execution.Elapsed, requestHeaders,
                    preview.HeadersText, preview.BodyPreview, preview.BodyWasTruncated)));

            var unresolved = Token().Matches(Uri.UnescapeDataString(sent.Url.OriginalString) + "\n" + ResponsePreview.FormatHeaders(sent.Headers))
                .Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();
            if (unresolved.Length > 0)
                log.Add(new(ConsoleEntryKind.Warning, now, Redact(string.Format(CultureInfo.CurrentCulture,
                    DefaultStrings.Get("Str.Console.Unresolved"), name, string.Join(", ", unresolved)))));
            if (execution.BodyWasTruncated)
                log.Add(new(ConsoleEntryKind.Warning, now, Format("Str.Console.BodyTruncated", name)));
        }

        if (!execution.IsSuccess)
            log.Add(new(ConsoleEntryKind.Error, now, string.Format(CultureInfo.CurrentCulture, DefaultStrings.Get("Str.Console.TransportError"),
                name, Redact(execution.ErrorMessage ?? execution.FailureKind.ToString()))));

        foreach (var test in run.Tests.Where(test => !test.Passed && !test.Incomplete))
            log.Add(new(ConsoleEntryKind.Error, now, string.Format(CultureInfo.CurrentCulture, DefaultStrings.Get("Str.Console.TestFailed"),
                name, Redact(test.Label), Redact(test.FailureMessage ?? string.Empty))));
    }

    private static string Format(string key, string value) =>
        string.Format(CultureInfo.CurrentCulture, DefaultStrings.Get(key), value);
}
