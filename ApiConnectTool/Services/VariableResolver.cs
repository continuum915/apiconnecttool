using System.Collections.Generic;
using System.Text.RegularExpressions;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

public sealed partial class VariableResolver : IVariableResolver
{
    public VariableResolutionResult Resolve(string? template, IReadOnlyDictionary<string, string> variables)
    {
        if (string.IsNullOrEmpty(template))
        {
            return new VariableResolutionResult { Value = template ?? string.Empty };
        }

        List<string>? unresolved = null;
        List<string>? resolved = null;
        var value = TokenPattern().Replace(template, match =>
        {
            var name = match.Groups[1].Value.Trim();
            if (variables.TryGetValue(name, out var resolvedValue))
            {
                resolved ??= [];
                if (!resolved.Contains(name))
                {
                    resolved.Add(name);
                }

                return resolvedValue;
            }

            unresolved ??= [];
            if (!unresolved.Contains(name))
            {
                unresolved.Add(name);
            }

            return match.Value;
        });

        return new VariableResolutionResult
        {
            Value = value,
            UnresolvedVariableNames = unresolved ?? (IReadOnlyList<string>)[],
            ResolvedVariableNames = resolved ?? (IReadOnlyList<string>)[]
        };
    }

    [GeneratedRegex(@"\{\{\s*([^{}]+?)\s*\}\}")]
    private static partial Regex TokenPattern();
}
