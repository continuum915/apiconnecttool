using System.Collections.Generic;

namespace ApiConnectTool.Models;

public sealed class VariableResolutionResult
{
    public string Value { get; init; } = string.Empty;
    public IReadOnlyList<string> UnresolvedVariableNames { get; init; } = [];

    /// <summary>
    /// Names of variables that were actually substituted into <see cref="Value"/>. Lets a caller
    /// (e.g. secret-value collection) know which entries of the variable map were used without
    /// re-scanning the template itself.
    /// </summary>
    public IReadOnlyList<string> ResolvedVariableNames { get; init; } = [];
}
