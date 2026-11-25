using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace ApiConnectTool.Models;

public partial class EnvironmentVariable : ObservableObject
{
    public EnvironmentVariable Clone() => new() { Key = Key, InitialValue = InitialValue, CurrentValue = CurrentValue,
        IsSecret = IsSecret, IsEnabled = IsEnabled, HasCurrentValue = HasCurrentValue };
    public Guid Id { get; set; } = Guid.NewGuid();
    [ObservableProperty] private string _key = string.Empty;

    // Saved local default; used when no override is set. Not automatically team-shared.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(InitialValueText))] private EncryptedString _initialValue = new(string.Empty);

    // Persisted local override; takes precedence over InitialValue when resolving {{variables}}.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CurrentValueText)), NotifyPropertyChangedFor(nameof(UseOverride))] private EncryptedString _currentValue = new(string.Empty);
    [ObservableProperty, NotifyPropertyChangedFor(nameof(UseOverride))] private bool _hasCurrentValue;
    [ObservableProperty] private bool _isSecret;
    [ObservableProperty] private bool _isEnabled = true;
    [JsonIgnore] public string InitialValueText { get => InitialValue.Value; set { if (value != InitialValue.Value) InitialValue = value; } }
    [JsonIgnore] public string CurrentValueText { get => CurrentValue.Value; set { if (value == CurrentValue.Value) return; CurrentValue = value; HasCurrentValue = true; } }
    [JsonIgnore]
    public bool UseOverride
    {
        get => HasCurrentValue || !string.IsNullOrEmpty(CurrentValue.Value);
        set
        {
            if (value == UseOverride) return;
            if (value) HasCurrentValue = true;
            else { CurrentValue = string.Empty; HasCurrentValue = false; }
        }
    }
}

public class RequestEnvironment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    // Drives the configurable warning before sending against this environment.
    public bool IsProduction { get; set; }
    public ObservableCollection<EnvironmentVariable> Variables { get; set; } = new();

    /// <summary>
    /// Builds the {{name}} -> value map used by variable resolution. Persisted local CurrentValue
    /// takes precedence over the saved InitialValue when both are set; disabled or keyless rows
    /// are excluded entirely.
    /// </summary>
    public IReadOnlyDictionary<string, string> BuildVariableMap()
    {
        var map = new Dictionary<string, string>();
        foreach (var variable in Variables)
        {
            if (!variable.IsEnabled || string.IsNullOrWhiteSpace(variable.Key))
            {
                continue;
            }

            var current = variable.CurrentValue.Value;
            map[variable.Key] = variable.HasCurrentValue || !string.IsNullOrEmpty(current) ? current : variable.InitialValue.Value;
        }

        return map;
    }

    /// <summary>
    /// Names of enabled variables flagged <see cref="EnvironmentVariable.IsSecret"/>, for
    /// <see cref="Models.RequestExecutionOptions.SecretVariableNames"/> - lets execution know which
    /// resolved values must be treated as secrets without re-deriving it from <see cref="Variables"/>.
    /// </summary>
    public IReadOnlySet<string> BuildSecretVariableNames() =>
        Variables.Where(v => v.IsEnabled && v.IsSecret && !string.IsNullOrWhiteSpace(v.Key))
            .Select(v => v.Key)
            .ToHashSet();
}
