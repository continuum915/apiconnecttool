using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ApiConnectTool.Services.Localization;
using ApiConnectTool.ViewModels;

namespace ApiConnectTool.Views;

public partial class ImportWizardWindow : Window
{
    public ImportWizardWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ImportWizardViewModel viewModel) viewModel.CloseRequested += Close;
        };
    }

    private ImportWizardViewModel? ViewModel => DataContext as ImportWizardViewModel;

    private void OnFormatClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && sender is Control { Tag: ImportFormatChoice choice }) viewModel.SelectedFormat = choice;
    }

    private void OnNewCollectionClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel) viewModel.ImportIntoExisting = false;
    }

    private void OnPasteClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel) viewModel.IsFromFile = false;
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = AppLocalization.Get("Str.ImportWizard.Title"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(viewModel.SelectedFormat.Name) { Patterns = ["*.json", "*.yaml", "*.yml", "*.txt", "*.sh"] },
                new FilePickerFileType(AppLocalization.Get("Str.ImportWizard.AllFiles")) { Patterns = ["*"] }
            ]
        });
        if (files.Count == 0) return;
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            viewModel.IsFromFile = true;
            viewModel.LoadFile(files[0].TryGetLocalPath() ?? files[0].Name, await reader.ReadToEndAsync());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            viewModel.SourceError = string.Format(AppLocalization.Get("Str.ImportWizard.ReadError"), error.Message);
        }
    }

    /// <summary>Opens the wizard, on the Format step or, with a format, on the Source step.</summary>
    public static Task ShowAsync(Window owner, CollectionsViewModel panel, ApiConnectTool.Services.ImportFormat? format = null) =>
        new ImportWizardWindow { DataContext = new ImportWizardViewModel(panel, format) }.ShowDialog(owner);
}
