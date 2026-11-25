using System.Reflection;
using Avalonia.Controls;

namespace ApiConnectTool.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    public static new void Show(Window owner)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var window = new AboutWindow();
        window.VersionText.Text = $"Version {(version?.ToString() ?? "unknown")}";
        window.ShowDialog(owner);
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
