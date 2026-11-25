using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using ApiConnectTool.Models;
using ApiConnectTool.Services;

namespace ApiConnectTool.Views;

public partial class ProductionSendConfirmationWindow : Window
{
    public ProductionSendConfirmationWindow()
    {
        InitializeComponent();
    }

    public static Task<bool> ShowAsync(Window owner, IReadOnlyList<ProductionTarget> targets)
    {
        var window = new ProductionSendConfirmationWindow();
        window.MessageText.Text = ProductionSendMessage.Format(targets);
        return ConfirmationDialogHelper.ShowAsync(window, owner, window.SendButton, window.CancelButton);
    }
}
