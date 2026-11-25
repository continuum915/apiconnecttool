using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApiConnectTool.Services;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.ViewModels;

/// <summary>Startup banner listing where secrets that could not be decrypted live. Lists
/// locations only; values and stored payloads are never part of the text.</summary>
public partial class UndecryptableSecretsBannerViewModel : ViewModelBase
{
    [ObservableProperty] private string? _warning;

    public UndecryptableSecretsBannerViewModel(IReadOnlyList<UndecryptableSecretLocation> locations)
    {
        if (locations.Count == 0) return;
        Warning = string.Join(System.Environment.NewLine, locations.Select(location => string.Format(
            AppLocalization.Get($"Str.Workspace.UndecryptableSecret.{location.Kind}"),
            location.OwnerName,
            AppLocalization.Get($"Str.Workspace.UndecryptableSecret.Field.{location.Field}"),
            location.VariableKey,
            location.CollectionName)));
    }

    [RelayCommand]
    private void Dismiss() => Warning = null;
}
