using System.Collections.Generic;

namespace ApiConnectTool.Services;

public interface ISecretRedactor
{
    /// <summary>
    /// Replaces every occurrence of each value in <paramref name="knownSecretValues"/> and its
    /// deterministic percent-encoded and JSON-escaped forms with a fixed-width placeholder.
    /// Deliberately not a heuristic/entropy scanner - only exact,
    /// known-at-resolution-time secret literals (see
    /// <see cref="Models.RequestExecutionResult.ResolvedSecretValues"/>) are redacted, so behavior
    /// stays deterministic and testable.
    /// </summary>
    string Redact(string text, IEnumerable<string> knownSecretValues);
}
