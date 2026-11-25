using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ApiConnectTool.Views.Components
{
    public partial class AppToolbarControl : UserControl
    {
        public AppToolbarControl()
        {
            InitializeComponent();
        }

        private async void OnImportClick(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is MainWindow window) await window.OpenImportWizardAsync();
        }
    }
}
