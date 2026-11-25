using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Models;

public sealed record AssertionResult(Guid SourceId, string Label, bool Passed, string ActualValue, string? FailureMessage)
{
    /// <summary>The check depended on a response body that was truncated, so it was not evaluated.</summary>
    public bool Incomplete { get; init; }

    public string StatusText => AppLocalization.Get(Incomplete ? "Str.Assertions.Incomplete" : Passed ? "Str.Assertions.Pass" : "Str.Assertions.Fail");

    public static AssertionResult IncompleteFor(Guid sourceId, string label, string message) =>
        new(sourceId, label, false, "", message) { Incomplete = true };

    public static string Summarize(IReadOnlyCollection<AssertionResult> results)
    {
        var passed = results.Count(result => result.Passed);
        var incomplete = results.Count(result => result.Incomplete);
        return incomplete > 0
            ? string.Format(CultureInfo.CurrentCulture, AppLocalization.Get("Str.Assertions.SummaryIncomplete"), passed, results.Count, incomplete)
            : string.Format(CultureInfo.CurrentCulture, AppLocalization.Get("Str.Assertions.Summary"), passed, results.Count);
    }
}
