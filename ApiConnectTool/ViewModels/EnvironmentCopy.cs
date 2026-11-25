using System.Diagnostics.CodeAnalysis;
using ApiConnectTool.Models;

namespace ApiConnectTool.ViewModels;

/// <summary>The safe-duplication rules shared by Duplicate and by copying or moving a global
/// environment into a collection. The copy gets a new ID. Secret definitions and flags are kept,
/// but secret values are copied only when <c>includeSecretValues</c> is set, and local overrides
/// only when <c>copyOverrides</c> is set.</summary>
public static class EnvironmentCopy
{
    /// <summary>Returns false, with no copy, when a value that would be copied cannot be decrypted.</summary>
    public static bool TryCreate(RequestEnvironment source, string name, bool copyOverrides, bool includeSecretValues, [NotNullWhen(true)] out RequestEnvironment? copy)
    {
        var result = new RequestEnvironment { Name = name, IsProduction = source.IsProduction };
        copy = null;
        foreach (var row in source.Variables)
        {
            var includeValues = !row.IsSecret || includeSecretValues;
            var includeOverride = includeValues && copyOverrides && row.UseOverride;
            if (includeValues && (row.InitialValue.FailedToDecrypt || (copyOverrides && row.CurrentValue.FailedToDecrypt)))
            {
                return false;
            }

            result.Variables.Add(new()
            {
                Key = row.Key, IsEnabled = row.IsEnabled, IsSecret = row.IsSecret,
                InitialValue = includeValues ? row.InitialValue : string.Empty,
                CurrentValue = includeOverride ? row.CurrentValue : string.Empty,
                HasCurrentValue = includeOverride && row.HasCurrentValue
            });
        }

        copy = result;
        return true;
    }
}
