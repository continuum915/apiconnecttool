using Avalonia.Controls;
using ApiConnectTool.ViewModels;
namespace ApiConnectTool.Views;
public partial class CollectionSettingsWindow : Window
{
    public CollectionSettingsWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as CollectionSettingsViewModel)?.FlushChanges();
    }
}
