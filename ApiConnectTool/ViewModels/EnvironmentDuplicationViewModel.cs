using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.ViewModels;

public partial class EnvironmentDuplicationViewModel : ObservableObject
{
    private readonly RequestEnvironment _source;
    private readonly IEnumerable<RequestEnvironment> _environments;
    private readonly Action<RequestEnvironment> _onCreated;
    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _copyOverrides;
    [ObservableProperty] private bool _includeSecretValues;
    [ObservableProperty] private string? _error;
    public bool CanDuplicate => NameError is null;
    private string? NameError => string.IsNullOrWhiteSpace(Name)
        ? AppLocalization.Get("Str.EnvironmentDuplicate.NameRequired")
        : _environments.Any(x => string.Equals(x.Name.Trim(), Name.Trim(), StringComparison.OrdinalIgnoreCase))
            ? AppLocalization.Get("Str.EnvironmentDuplicate.NameConflict") : null;

    public EnvironmentDuplicationViewModel(RequestEnvironment source, IEnumerable<RequestEnvironment> environments, Action<RequestEnvironment> onCreated)
    {
        _source = source;
        _environments = environments;
        _onCreated = onCreated;
        var baseName = string.Format(AppLocalization.Get("Str.EnvironmentDuplicate.SuggestedName"), source.Name.Trim());
        _name = baseName;
        var suffix = 2;
        while (NameError is not null) _name = $"{baseName} {suffix++}";
    }
    partial void OnNameChanged(string value) { Error = NameError; OnPropertyChanged(nameof(CanDuplicate)); }
    partial void OnCopyOverridesChanged(bool value) => Error = NameError;
    partial void OnIncludeSecretValuesChanged(bool value) => Error = NameError;

    public bool TryDuplicate()
    {
        Error = NameError;
        if (Error is not null) return false;
        if (!EnvironmentCopy.TryCreate(_source, Name.Trim(), CopyOverrides, IncludeSecretValues, out var copy))
        {
            Error = AppLocalization.Get("Str.EnvironmentDuplicate.DecryptionFailed");
            return false;
        }
        _onCreated(copy);
        return true;
    }
}
