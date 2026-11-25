using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ApiConnectTool.ViewModels;

namespace ApiConnectTool.Views;

public partial class VariableInspectorWindow : Window
{
    public VariableInspectorWindow() => InitializeComponent();

    public VariableInspectorWindow(VariableInspectorViewModel inspector) : this()
    {
        DataContext = inspector;
        Closed += (_, _) => inspector.Dispose();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }
}
