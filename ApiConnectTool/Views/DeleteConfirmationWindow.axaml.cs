using System.Threading.Tasks;
using Avalonia.Controls;

namespace ApiConnectTool.Views;

public partial class DeleteConfirmationWindow : Window
{
    public DeleteConfirmationWindow() => InitializeComponent();

    public static Task<bool> ShowAsync(Window owner, string itemDescription)
    {
        var window = new DeleteConfirmationWindow();
        window.MessageText.Text = $"Delete {itemDescription}? This cannot be undone.";
        return ConfirmationDialogHelper.ShowAsync(window, owner, window.DeleteButton, window.CancelButton);
    }
}
