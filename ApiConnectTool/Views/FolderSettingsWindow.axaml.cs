using Avalonia.Controls;
using ApiConnectTool.ViewModels;
namespace ApiConnectTool.Views;
public partial class FolderSettingsWindow : Window
{
    public FolderSettingsWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as FolderSettingsViewModel)?.FlushChanges();
    }
}
