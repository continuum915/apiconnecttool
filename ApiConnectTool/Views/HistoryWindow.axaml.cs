using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ApiConnectTool.Services.Localization;
using ApiConnectTool.ViewModels;

namespace ApiConnectTool.Views;

public partial class HistoryWindow : Window
{
    public HistoryWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not HistoryWindowViewModel viewModel) return;
            viewModel.ConfirmClear = ConfirmClearAsync;
            viewModel.ShowComparison = comparison => new ResponseComparisonWindow { DataContext = comparison }.ShowDialog(this);
        };
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Closed += (_, _) => (DataContext as HistoryWindowViewModel)?.CancelCompareCommand.Execute(null);
    }

    private Task<bool> ConfirmClearAsync() =>
        DeleteConfirmationWindow.ShowAsync(this, AppLocalization.Get("Str.HistoryWindow.ClearTarget"));

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is HistoryWindowViewModel { IsPickingCompare: true } viewModel)
        {
            viewModel.CancelCompareCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e) =>
        (DataContext as HistoryWindowViewModel)?.ResendCommand.Execute(null);
}
