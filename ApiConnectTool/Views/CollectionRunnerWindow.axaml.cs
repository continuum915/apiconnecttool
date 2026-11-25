using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ApiConnectTool.Services.Localization;
using ApiConnectTool.ViewModels;

namespace ApiConnectTool.Views;

public partial class CollectionRunnerWindow : Window
{
    public CollectionRunnerWindow()
    {
        InitializeComponent();
        Closing += (_, _) => (DataContext as CollectionRunnerViewModel)?.Close();
    }

    /// <summary>Failed and errored results get the danger row background (rows are recycled, so
    /// the class is set each time a row is prepared).</summary>
    private void OnResultRowLoading(object? sender, DataGridRowEventArgs e) =>
        e.Row.Classes.Set("problem", e.Row.DataContext is ApiConnectTool.Models.CollectionRunEntry { Outcome: not ApiConnectTool.Models.RunEntryOutcome.Passed });

    private async void OnChooseDataClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CollectionRunnerViewModel viewModel) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = AppLocalization.Get("Str.Runner.ChooseData"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(AppLocalization.Get("Str.Runner.DataFiles")) { Patterns = ["*.csv", "*.json"] }]
        });
        if (files.Count == 0) return;
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            viewModel.SetIterationData(files[0].Name, await reader.ReadToEndAsync());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            viewModel.ShowDataError(string.Format(AppLocalization.Get("Str.Runner.ReadError"), error.Message));
        }
    }
}
