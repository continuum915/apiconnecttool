using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Services.Localization;
using ApiConnectTool.ViewModels;

namespace ApiConnectTool.Views;

/// <summary>Window-level actions shared by the menu bar, the request editor and the navigator:
/// copy as cURL, the variable inspector and collection export. Each needs a file picker, the
/// clipboard or an owned window, so it lives with the views.</summary>
public static class RequestActions
{
    public static async Task CopyAsCurlAsync(TopLevel topLevel, RequestTabViewModel tab, bool revealSecrets)
    {
        if (topLevel.Clipboard is null) return;
        await topLevel.Clipboard.SetTextAsync(tab.BuildCurlCommand(revealSecrets));
        tab.SetResponseActionMessage(AppLocalization.Get(revealSecrets ? "Str.Curl.CopiedRevealed" : "Str.Curl.CopiedRedacted"));
    }

    /// <summary>Opens the variable inspector for <paramref name="tab"/>, or activates the open one.
    /// When the owner is bound to the main view model, the inspector follows tab and environment
    /// switches and can navigate to the scope that defines a variable.</summary>
    public static void InspectVariables(Window owner, RequestTabViewModel tab)
    {
        if (owner.OwnedWindows.OfType<VariableInspectorWindow>().FirstOrDefault() is { } existing)
        {
            existing.Activate();
            return;
        }
        var inspector = tab.InspectVariables();
        var dialog = new VariableInspectorWindow(inspector);
        if (owner.DataContext is MainWindowViewModel main)
        {
            inspector.SetRefresh(() => main.ActiveTab?.InspectVariables(inspector)
                ?? new VariableInspectorViewModel(new RequestItem(), VariableScopeBuilder.Build()));
            PropertyChangedEventHandler changed = (_, args) =>
            {
                if (args.PropertyName is nameof(MainWindowViewModel.ActiveTab) or nameof(MainWindowViewModel.SelectedEnvironment))
                    inspector.Refresh();
            };
            main.PropertyChanged += changed;
            dialog.Closed += (_, _) => main.PropertyChanged -= changed;
            inspector.Navigate += scope => NavigateToScope(main, dialog, scope);
            inspector.EnableNavigation();
        }
        dialog.Show(owner);
    }

    private static void NavigateToScope(MainWindowViewModel main, Window dialog, VariableScopeKind scope)
    {
        if (scope == VariableScopeKind.Collection)
        {
            if (main.ActiveTab?.CollectionProvider?.Invoke() is { } collection)
                new CollectionSettingsWindow { DataContext = main.CreateCollectionSettings(collection) }.Show(dialog);
        }
        else if (scope == VariableScopeKind.CollectionEnvironment)
        {
            if (main.ActiveTab?.CollectionProvider?.Invoke() is { } collection)
                new EnvironmentEditorWindow { DataContext = main.CreateEnvironmentEditor(collection) }.Show(dialog);
        }
        else
        {
            var editor = main.CreateEnvironmentEditor();
            editor.SelectedEnvironment = scope == VariableScopeKind.Globals ? editor.Globals : main.SelectedEnvironment;
            new EnvironmentEditorWindow { DataContext = editor }.Show(dialog);
        }
    }

    /// <summary>Exports <paramref name="collection"/> with secrets redacted, in the native or the
    /// Postman format, and reports the outcome through the collections panel message.</summary>
    public static async Task ExportCollectionAsync(TopLevel topLevel, CollectionsViewModel panel, RequestCollection collection, bool asPostman)
    {
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = AppLocalization.Get(asPostman ? "Str.Export.PostmanTitle" : "Str.Export.Title"),
            SuggestedFileName = asPostman ? $"{collection.Name}.postman_collection.json" : $"{collection.Name}.apiconnecttoolcollection.json",
            FileTypeChoices = [new FilePickerFileType(AppLocalization.Get(asPostman ? "Str.Export.PostmanFileType" : "Str.Export.FileType")) { Patterns = ["*.json"] }]
        });
        if (file is null) return;

        var localPath = file.TryGetLocalPath();
        if (localPath is null)
        {
            panel.ImportExportMessage = AppLocalization.Get("Str.Export.LocalDestination");
            return;
        }

        try
        {
            // Secrets are never exported by default; there is no opt-in on these commands.
            var json = asPostman
                ? panel.ExportCollectionAsPostman(collection, includeSecrets: false)
                : panel.ExportCollection(collection, includeSecrets: false);
            await File.WriteAllTextAsync(localPath, json);
            var message = string.Format(AppLocalization.Get(asPostman ? "Str.Export.PostmanSucceeded" : "Str.Export.Succeeded"),
                collection.Name, Path.GetFileName(localPath));
            if (asPostman && CollectionsViewModel.PostmanExportNote(collection) is { } note) message += " " + note;
            panel.ImportExportMessage = message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            panel.ImportExportMessage = string.Format(AppLocalization.Get("Str.Export.Failed"), ex.Message);
        }
    }
}
