using System.Collections.Generic;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

public sealed record VariableScopeMap(IReadOnlyDictionary<string, string> Variables, IReadOnlySet<string> SecretNames)
{
    public IReadOnlyList<VariableDefinition> Definitions { get; init; } = [];
}

/// <summary>Where a variable definition came from. <see cref="Environment"/> is the active global
/// environment; <see cref="CollectionEnvironment"/> is the active environment of the request's collection.</summary>
public enum VariableScopeKind { Globals, Collection, Environment, Run, CollectionEnvironment }

public sealed class VariableDefinition
{
    public required string Name { get; init; }
    public required string ScopeName { get; init; }
    public required VariableScopeKind ScopeKind { get; init; }
    public required string DefaultValue { get; init; }
    public required string OverrideValue { get; init; }
    public bool IsOverride { get; init; }
    public bool IsEnabled { get; init; }
    public bool IsSecret { get; init; }
    public bool DefaultFailedToDecrypt { get; init; }
    public bool OverrideFailedToDecrypt { get; init; }
    public bool ValueFailedToDecrypt => IsOverride ? OverrideFailedToDecrypt : DefaultFailedToDecrypt;
    public bool IsWinner { get; internal set; }
    public string Value => IsOverride ? OverrideValue : DefaultValue;
}

public static class VariableScopeBuilder
{
    /// <summary>Layers the scopes from lowest to highest precedence: globals, collection variables,
    /// the active global environment (<paramref name="environment"/>), the active collection
    /// environment (<paramref name="collectionEnvironment"/>), then run values.</summary>
    public static VariableScopeMap Build(Workspace? workspace = null, RequestCollection? collection = null,
        RequestEnvironment? environment = null, IReadOnlyDictionary<string, string>? run = null,
        RequestEnvironment? collectionEnvironment = null)
    {
        var values = new Dictionary<string, string>();
        var secrets = new HashSet<string>();
        var definitions = new List<VariableDefinition>();
        var winners = new Dictionary<string, VariableDefinition>();
        void Add(VariableDefinition definition)
        {
            definitions.Add(definition);
            if (!definition.IsEnabled || (definition.ScopeKind != VariableScopeKind.Run && string.IsNullOrWhiteSpace(definition.Name))) return;
            if (winners.TryGetValue(definition.Name, out var previous)) previous.IsWinner = false;
            definition.IsWinner = true;
            winners[definition.Name] = definition;
            values[definition.Name] = definition.Value;
            if (definition.IsSecret) secrets.Add(definition.Name);
        }
        foreach (var (scope, name, kind) in new[] {
            (workspace?.GlobalVariables, "Globals", VariableScopeKind.Globals),
            (collection?.Variables, collection?.Name ?? "", VariableScopeKind.Collection),
            (environment?.Variables, environment?.Name ?? "", VariableScopeKind.Environment),
            (collectionEnvironment?.Variables, collectionEnvironment?.Name ?? "", VariableScopeKind.CollectionEnvironment) })
        {
            if (scope is null) continue;
            foreach (var variable in scope)
            {
                Add(new() { Name = variable.Key, ScopeName = name, ScopeKind = kind,
                    DefaultValue = variable.InitialValue.Value, OverrideValue = variable.CurrentValue.Value,
                    DefaultFailedToDecrypt = variable.InitialValue.FailedToDecrypt,
                    OverrideFailedToDecrypt = variable.CurrentValue.FailedToDecrypt,
                    IsOverride = variable.UseOverride, IsEnabled = variable.IsEnabled, IsSecret = variable.IsSecret });
            }
        }
        if (run is not null) foreach (var (key, value) in run)
            Add(new() { Name = key, ScopeName = "Run", ScopeKind = VariableScopeKind.Run, DefaultValue = value,
                OverrideValue = value, IsOverride = true, IsEnabled = true });
        return new(values, secrets) { Definitions = definitions };
    }
}
