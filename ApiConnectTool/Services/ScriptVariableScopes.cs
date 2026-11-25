using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

public sealed class ScriptVariableScopes(Workspace? workspace, RequestCollection? collection, RequestEnvironment? environment,
    Action? save = null, SynchronizationContext? ownerContext = null, RequestEnvironment? collectionEnvironment = null)
{
    private readonly SynchronizationContext? _ownerContext = ownerContext ?? SynchronizationContext.Current;
    private void OnOwner(Action action)
    {
        if (_ownerContext is null || ReferenceEquals(SynchronizationContext.Current, _ownerContext)) action();
        else _ownerContext.Send(_ => action(), null);
    }
    private T ReadOnOwner<T>(Func<T> read)
    {
        T result = default!;
        OnOwner(() => result = read());
        return result;
    }
    public VariableScopeMap Build(IReadOnlyDictionary<string, string>? run = null) => ReadOnOwner(() => VariableScopeBuilder.Build(workspace, collection, environment, run, collectionEnvironment));
    // pm.environment writes go to the active collection environment when one is selected, else the global one.
    private ObservableCollection<EnvironmentVariable>? Rows(string name) => name switch
    { "environment" => (collectionEnvironment ?? environment)?.Variables, "collectionVariables" => collection?.Variables, "globals" => workspace?.GlobalVariables, _ => null };
    // pm.environment reads the active collection environment first, then the active global environment.
    public object Seed() => ReadOnOwner(() => new Dictionary<string, object?> {
        ["environment"] = environment is null && collectionEnvironment is null ? null
            : new Dictionary<string, string>(VariableScopeBuilder.Build(environment: environment, collectionEnvironment: collectionEnvironment).Variables),
        ["collectionVariables"] = Map(collection?.Variables), ["globals"] = Map(workspace?.GlobalVariables) });
    private static Dictionary<string, string>? Map(ObservableCollection<EnvironmentVariable>? rows) => rows is null ? null
        : new(VariableScopeBuilder.Build(environment: new() { Variables = rows }).Variables);
    public IReadOnlyList<string> SecretValues() => ReadOnOwner(() => new[] { workspace?.GlobalVariables, collection?.Variables, environment?.Variables, collectionEnvironment?.Variables }
        .Where(rows => rows is not null).SelectMany(rows => rows!).Where(row => row.IsSecret)
        .SelectMany(row => new[] { row.InitialValue.Value, row.CurrentValue.Value }).Where(value => !string.IsNullOrEmpty(value)).ToArray());
    public IReadOnlySet<string> SecretNames() => ReadOnOwner(() => new[] { workspace?.GlobalVariables, collection?.Variables, environment?.Variables, collectionEnvironment?.Variables }
        .Where(rows => rows is not null).SelectMany(rows => rows!).Where(row => row.IsSecret).Select(row => row.Key).ToHashSet());
    public void Apply(string scope, string key, string? value) => OnOwner(() => ApplyValue(scope, key, value));
    private void ApplyValue(string scope, string key, string? value)
    {
        var rows = Rows(scope) ?? throw new InvalidOperationException(string.Format(Localization.DefaultStrings.Get("Str.Scripts.ScopeUnavailable"), scope));
        var row = rows.LastOrDefault(row => row.Key == key);
        if (value is null)
        {
            foreach (var match in rows.Where(row => row.Key == key).ToArray()) rows.Remove(match);
        }
        else
        {
            if (row is null) { row = new() { Key = key }; rows.Add(row); }
            row.CurrentValue = value;
            row.HasCurrentValue = true;
            row.IsEnabled = true;
        }
    }
    public void Save() { if (save is not null) OnOwner(save); }
}
