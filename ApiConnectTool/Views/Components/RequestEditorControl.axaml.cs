using System.Threading.Tasks;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;
using ApiConnectTool.ViewModels;

namespace ApiConnectTool.Views.Components
{
    public partial class RequestEditorControl : UserControl
    {
        public RequestEditorControl()
        {
            InitializeComponent();
        }

        public void FocusUrlBox()
        {
            UrlTextBox.Focus();
            UrlTextBox.SelectAll();
        }

        private async Task<string?> ChooseBodyFileAsync()
        {
            if (TopLevel.GetTopLevel(this) is not { } topLevel) return null;
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new() { Title = AppLocalization.Get("Str.FormData.Browse"), AllowMultiple = false });
            return files.Count == 0 ? null : files[0].TryGetLocalPath();
        }

        private async void OnBrowseFormFileClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is Button { DataContext: KeyValueItem row } && await ChooseBodyFileAsync() is { } path) row.FilePath = path;
        }

        private async void OnBrowseBinaryFileClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is RequestTabViewModel tab && await ChooseBodyFileAsync() is { } path) tab.BodyFilePath = path;
        }

        private async void OnCopyAsCurlRedactedClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => await CopyAsCurlAsync(revealSecrets: false);

        private async void OnCopyAsCurlRevealedClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => await CopyAsCurlAsync(revealSecrets: true);

        private async Task CopyAsCurlAsync(bool revealSecrets)
        {
            if (DataContext is RequestTabViewModel viewModel && TopLevel.GetTopLevel(this) is { } topLevel)
                await RequestActions.CopyAsCurlAsync(topLevel, viewModel, revealSecrets);
        }
    }
}
