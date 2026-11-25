using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.ViewModels;

/// <summary>Copies or moves a global environment into a collection. The copy follows the
/// Duplicate rules for secrets and overrides (<see cref="EnvironmentCopy"/>) and gets a new ID.
/// This view model only validates and builds the copy; the environment editor applies it.</summary>
public sealed partial class EnvironmentTransferViewModel : ObservableObject
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanTransfer))] private RequestCollection _targetCollection;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanTransfer))] private string _name;
    [ObservableProperty] private bool _copyOverrides;
    [ObservableProperty] private bool _includeSecretValues;
    [ObservableProperty] private string? _error;

    public EnvironmentTransferViewModel(RequestEnvironment source, IReadOnlyList<RequestCollection> collections, bool isMove)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (collections.Count == 0) throw new ArgumentException("A transfer needs at least one collection.", nameof(collections));
        Source = source;
        Collections = collections;
        IsMove = isMove;
        _targetCollection = collections[0];
        _name = source.Name.Trim();
        var suffix = 2;
        while (NameError is not null) _name = $"{source.Name.Trim()} {suffix++}";
    }

    public RequestEnvironment Source { get; }
    public IReadOnlyList<RequestCollection> Collections { get; }
    public bool IsMove { get; }
    public string Title => AppLocalization.Get(IsMove ? "Str.EnvironmentTransfer.MoveTitle" : "Str.EnvironmentTransfer.CopyTitle");
    public string ConfirmText => AppLocalization.Get(IsMove ? "Str.EnvironmentTransfer.Move" : "Str.EnvironmentTransfer.Copy");
    public bool CanTransfer => NameError is null;

    private string? NameError => string.IsNullOrWhiteSpace(Name)
        ? AppLocalization.Get("Str.EnvironmentDuplicate.NameRequired")
        : TargetCollection.Environments.Any(environment => string.Equals(environment.Name.Trim(), Name.Trim(), StringComparison.OrdinalIgnoreCase))
            ? AppLocalization.Get("Str.EnvironmentTransfer.NameConflict") : null;

    partial void OnNameChanged(string value) => Error = NameError;
    partial void OnTargetCollectionChanged(RequestCollection value) => Error = NameError;

    /// <summary>Checks the name against the target collection. Call before asking to confirm a move.</summary>
    public bool Validate()
    {
        Error = NameError;
        return Error is null;
    }

    /// <summary>Builds the copy, or returns null and sets <see cref="Error"/> when the name is
    /// invalid or a value to copy cannot be decrypted. Changes nothing.</summary>
    public RequestEnvironment? TryCreateCopy()
    {
        if (!Validate()) return null;
        if (EnvironmentCopy.TryCreate(Source, Name.Trim(), CopyOverrides, IncludeSecretValues, out var copy)) return copy;
        Error = AppLocalization.Get("Str.EnvironmentDuplicate.DecryptionFailed");
        return null;
    }
}
