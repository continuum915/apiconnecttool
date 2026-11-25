using System.Threading.Tasks;
using Avalonia.Controls;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Views;

public partial class DownloadConfirmationWindow : Window
{
    public DownloadConfirmationWindow() => InitializeComponent();

    public static Task<bool> ShowAsync(Window owner, string method)
    {
        var window = new DownloadConfirmationWindow();
        window.MessageText.Text = string.Format(AppLocalization.Get("Str.Response.DownloadConfirmMessage"), method);
        return ConfirmationDialogHelper.ShowAsync(window, owner, window.SendButton, window.CancelButton);
    }
}
