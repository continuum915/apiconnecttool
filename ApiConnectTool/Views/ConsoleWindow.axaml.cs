using Avalonia.Controls;
using ApiConnectTool.ViewModels;

namespace ApiConnectTool.Views;

public partial class ConsoleWindow : Window
{
    public ConsoleWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as ConsoleWindowViewModel)?.Dispose();
    }
}
