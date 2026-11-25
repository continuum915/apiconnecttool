using System;
using System.Collections.Generic;
using System.Linq;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Models;

public enum RunEntryOutcome { Passed, Failed, Errored }
public enum CollectionRunEndState { Completed, Cancelled, StoppedOnFailure }

public sealed record CollectionRunEntry(Guid RequestId, string RequestName, int IterationIndex,
    int? StatusCode, TimeSpan Elapsed, IReadOnlyList<AssertionResult> Tests,
    RunEntryOutcome Outcome, string? ErrorMessage)
{
    public string OutcomeText => AppLocalization.Get("Str.Runner." + Outcome);
    public string TestsSummary => AssertionResult.Summarize(Tests.ToArray());
    public string ElapsedText => $"{Elapsed.TotalMilliseconds:0} ms";
    public string Method => Details?.Method ?? string.Empty;

    /// <summary>What the details pane shows for this result, already redacted with the send's
    /// secrets. Kept in memory for the open runner window only; never persisted.</summary>
    public RunEntryDetails? Details { get; init; }
}

/// <summary>The redacted request and response of one run result, with its script console output.
/// The response body is capped like a history preview.</summary>
public sealed record RunEntryDetails(string Method, string RequestUrl, string RequestHeaders,
    string ResponseHeaders, string ResponseBody, bool ResponseBodyTruncated, IReadOnlyList<ScriptConsoleLine> Console);

public sealed record CollectionRunReport(IReadOnlyList<CollectionRunEntry> Entries,
    CollectionRunEndState EndState, TimeSpan TotalElapsed)
{
    public int RunCount => Entries.Count;
    public int PassedCount => Entries.Count(entry => entry.Outcome == RunEntryOutcome.Passed);
    public int FailedCount => Entries.Count(entry => entry.Outcome == RunEntryOutcome.Failed);
    public int ErroredCount => Entries.Count(entry => entry.Outcome == RunEntryOutcome.Errored);
}

public sealed record CollectionRunProgress(string CurrentRequest, int RunCount, int PassedCount,
    int FailedCount, int ErroredCount, CollectionRunEntry Entry);
