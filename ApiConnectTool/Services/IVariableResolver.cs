using System.Collections.Generic;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

public interface IVariableResolver
{
    /// <summary>
    /// Substitutes each <c>{{name}}</c> token in <paramref name="template"/> with the matching entry
    /// from <paramref name="variables"/>. Tokens with no matching entry are left untouched (visibly
    /// wrong rather than silently blanked) and their names are reported in the result.
    /// </summary>
    VariableResolutionResult Resolve(string? template, IReadOnlyDictionary<string, string> variables);
}
