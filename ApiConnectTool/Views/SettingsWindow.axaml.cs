using Avalonia.Controls;
using ApiConnectTool.ViewModels;

namespace ApiConnectTool.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    public static void Show(Window owner, MainWindowViewModel viewModel)
    {
        var window = new SettingsWindow { DataContext = viewModel };
        window.ShowDialog(owner);
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
