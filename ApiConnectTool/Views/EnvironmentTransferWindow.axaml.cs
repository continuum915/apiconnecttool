using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ApiConnectTool.Services.Localization;
using ApiConnectTool.ViewModels;

namespace ApiConnectTool.Views;

/// <summary>Asks where to copy or move a global environment. Closing with true means the user
/// confirmed; for a move that includes confirming that the global original will be deleted.</summary>
public partial class EnvironmentTransferWindow : Window
{
    private bool _confirmed;

    public EnvironmentTransferWindow()
    {
        InitializeComponent();
        Opened += (_, _) => { NameTextBox.Focus(); NameTextBox.SelectAll(); };
    }

    public static Task<bool> ShowAsync(Window owner, EnvironmentTransferViewModel viewModel)
    {
        var window = new EnvironmentTransferWindow { DataContext = viewModel };
        var completion = new TaskCompletionSource<bool>();
        window.Closed += (_, _) => completion.TrySetResult(window._confirmed);
        window.ShowDialog(owner);
        return completion.Task;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EnvironmentTransferViewModel viewModel || !viewModel.Validate()) return;
        if (viewModel.IsMove && !await DeleteConfirmationWindow.ShowAsync(this, string.Format(CultureInfo.CurrentCulture,
                AppLocalization.Get("Str.EnvironmentTransfer.DeleteOriginal"), viewModel.Source.Name, viewModel.TargetCollection.Name))) return;
        _confirmed = true;
        Close();
    }
}
