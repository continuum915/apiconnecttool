using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using AvaloniaEdit.Highlighting;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Views.Components;

public partial class ResponseViewerControl : UserControl
{
    private RequestTabViewModel? _viewModel;

    public ResponseViewerControl()
    {
        InitializeComponent();
        AvaloniaEdit.Search.SearchPanel.Install(ResponseEditor);
        DataContextChanged += OnDataContextChanged;
        ActualThemeVariantChanged += (_, _) => UpdateHighlighting();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as RequestTabViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdateEditorText();
            UpdateHighlighting();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RequestTabViewModel.DisplayedResponseBody))
        {
            UpdateEditorText();
        }
        else if (e.PropertyName == nameof(RequestTabViewModel.ResponseContentType))
        {
            UpdateHighlighting();
        }
    }

    private void UpdateEditorText()
    {
        if (_viewModel is not null && ResponseEditor is not null)
        {
            ResponseEditor.Text = _viewModel.DisplayedResponseBody;
        }
    }

    private void UpdateHighlighting()
    {
        if (_viewModel is null || ResponseEditor is null) return;

        var contentType = _viewModel.ResponseContentType.ToLowerInvariant();
        var definitionName = contentType.Contains("xml") ? "XML" :
            contentType.Contains("html") ? "HTML" :
            contentType.Contains("css") ? "CSS" : contentType.Contains("javascript") ? "JavaScript" : null;
        var highlighting = contentType.Contains("json") ? HighlightingManager.Instance.GetDefinition("Json")
            : definitionName is null ? null : HighlightingManager.Instance.GetDefinition(definitionName);
        CodeHighlightingTheme.Apply(highlighting, this);
        ResponseEditor.SyntaxHighlighting = null;
        ResponseEditor.SyntaxHighlighting = highlighting;
    }

    private async void OnCopyResponseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel is null || !_viewModel.HasTextResponse) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is null) return;

        await topLevel.Clipboard.SetTextAsync(_viewModel.DisplayedResponseBody);
        _viewModel.SetResponseActionMessage(AppLocalization.Get("Str.Response.Copied"));
    }

    private async void OnSaveResponseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await SaveResponseAsync(AppLocalization.Get("Str.Response.Save"));

    private async void OnDownloadResponseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await DownloadResponseAsync();

    private async Task DownloadResponseAsync()
    {
        if (_viewModel is null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = AppLocalization.Get("Str.Response.Download"),
            SuggestedFileName = GetSuggestedFileName(_viewModel.ResponseContentType)
        });
        if (file is null) return;

        var localPath = file.TryGetLocalPath();
        if (localPath is null)
        {
            _viewModel.SetResponseActionMessage(AppLocalization.Get("Str.Response.DownloadLocalDestination"));
            return;
        }

        await _viewModel.DownloadResponseAsync(localPath,
            method => topLevel is Window owner ? DownloadConfirmationWindow.ShowAsync(owner, method) : Task.FromResult(false));
    }

    private async Task SaveResponseAsync(string title)
    {
        if (_viewModel is null || !_viewModel.CanSaveResponse) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = GetSuggestedFileName(_viewModel.ResponseContentType)
        });
        if (file is null) return;

        try
        {
            await using var stream = await file.OpenWriteAsync();
            if (await _viewModel.SaveResponseAsync(stream))
                _viewModel.SetResponseActionMessage(AppLocalization.Get("Str.Response.Saved"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _viewModel.SetResponseActionMessage(string.Format(AppLocalization.Get("Str.Response.SaveFailed"), ex.Message));
        }
    }

    private static string GetSuggestedFileName(string contentType) =>
        contentType.Contains("json", StringComparison.OrdinalIgnoreCase) ? "response.json" :
        contentType.Contains("xml", StringComparison.OrdinalIgnoreCase) ? "response.xml" :
        contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ? "response.txt" :
        "response.bin";
}
