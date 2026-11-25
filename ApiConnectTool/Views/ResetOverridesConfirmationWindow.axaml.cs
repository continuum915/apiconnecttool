using System.Threading.Tasks;
using Avalonia.Controls;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Views;

public partial class ResetOverridesConfirmationWindow : Window
{
    public ResetOverridesConfirmationWindow() => InitializeComponent();
    public static Task<bool> ShowAsync(Window owner, string scopeName, int count)
    {
        var window = new ResetOverridesConfirmationWindow();
        window.MessageText.Text = string.Format(AppLocalization.Get("Str.Variables.ResetAllConfirmation"), scopeName, count);
        window.Opened += (_, _) => window.CancelButton.Focus();
        return ConfirmationDialogHelper.ShowAsync(window, owner, window.ResetButton, window.CancelButton);
    }
}
