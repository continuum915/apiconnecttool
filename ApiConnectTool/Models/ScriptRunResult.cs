using System.Collections.Generic;
using System.Linq;

namespace ApiConnectTool.Models;

public enum ScriptOutcome { Completed, TimedOut, LimitExceeded, ScriptError }

public sealed record ScriptRunResult(ScriptOutcome Outcome, string? ErrorMessage,
    IReadOnlyList<AssertionResult> Tests, IReadOnlyDictionary<string, string> Variables,
    IReadOnlyList<string> SecretValues)
{
    /// <summary>Redacted <c>console.*</c> output, in order, bounded per run.</summary>
    public IReadOnlyList<ScriptConsoleLine> Console { get; init; } = [];
}

public sealed record RequestRunResult(RequestExecutionResult Execution,
    IReadOnlyList<AssertionResult> Tests, bool TestsEvaluated,
    IReadOnlyDictionary<string, string> Variables, IReadOnlySet<string> SecretVariableNames,
    IReadOnlyList<ScriptRunResult> Scripts)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public RequestReplay? Replay { get; init; }

    /// <summary>Pre-request then test script console output, redacted with the run's final secrets.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<ScriptConsoleLine> Console { get; init; } = [];

    /// <summary>Some body-dependent validation could not run because the response body was truncated.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ValidationIncomplete => Tests.Any(test => test.Incomplete);
}
