using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ApiConnectTool.ViewModels;

namespace ApiConnectTool.Views;

public partial class DuplicateEnvironmentWindow : Window
{
    private bool _created;
    public DuplicateEnvironmentWindow()
    {
        InitializeComponent();
        Opened += (_, _) => { NameTextBox.Focus(); NameTextBox.SelectAll(); };
    }
    public static Task<bool> ShowAsync(Window owner, EnvironmentDuplicationViewModel viewModel)
    {
        var window = new DuplicateEnvironmentWindow { DataContext = viewModel };
        var completion = new TaskCompletionSource<bool>();
        window.Closed += (_, _) => completion.TrySetResult(window._created);
        window.ShowDialog(owner);
        return completion.Task;
    }
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
    private void OnDuplicateClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EnvironmentDuplicationViewModel viewModel && viewModel.TryDuplicate())
        {
            _created = true;
            Close();
        }
    }
}
