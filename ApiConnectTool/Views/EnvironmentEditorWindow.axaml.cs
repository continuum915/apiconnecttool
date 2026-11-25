using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ApiConnectTool.ViewModels;

namespace ApiConnectTool.Views;

public partial class EnvironmentEditorWindow : Window
{
    public EnvironmentEditorWindow()
    {
        InitializeComponent();
        Closed += (_, _) =>
        {
            if (DataContext is EnvironmentEditorViewModel viewModel) { viewModel.FlushChanges(); viewModel.Dispose(); }
        };
    }

    private void OnEnvironmentSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Removing a filtered entry must not clear the manager's editing selection.
        if (DataContext is EnvironmentEditorViewModel editor && e.AddedItems.Count > 0 &&
            e.AddedItems[0] is ApiConnectTool.Models.RequestEnvironment environment)
            editor.SelectedEnvironment = environment;
    }

    private void OnEnvironmentNameChanged(object? sender, TextChangedEventArgs e)
    {
        if (DataContext is EnvironmentEditorViewModel editor)
            Dispatcher.UIThread.Post(editor.RefreshDisplay);
    }

    private async void OnDuplicateEnvironmentClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EnvironmentEditorViewModel viewModel || viewModel.CreateDuplication() is not { } duplication) return;
        if (await DuplicateEnvironmentWindow.ShowAsync(this, duplication))
        {
            Dispatcher.UIThread.Post(() =>
            {
                EnvironmentNameTextBox.Focus();
                EnvironmentNameTextBox.SelectAll();
            });
        }
    }

    private async void OnTransferEnvironmentClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EnvironmentEditorViewModel viewModel ||
            viewModel.CreateTransfer(move: (sender as Control)?.Tag as string == "move") is not { } transfer) return;
        if (await EnvironmentTransferWindow.ShowAsync(this, transfer)) viewModel.ApplyTransfer(transfer);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnDeleteEnvironmentClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EnvironmentEditorViewModel viewModel ||
            viewModel.SelectedEnvironment is not { } environment) return;

        if (await DeleteConfirmationWindow.ShowAsync(this, string.Format(System.Globalization.CultureInfo.CurrentCulture,
                ApiConnectTool.Services.Localization.AppLocalization.Get("Str.EnvironmentEditor.DeleteTarget"), environment.Name)))
        {
            viewModel.DeleteEnvironmentCommand.Execute(environment);
        }
    }
}
