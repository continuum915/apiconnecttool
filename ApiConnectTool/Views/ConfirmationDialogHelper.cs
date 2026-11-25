using System.Threading.Tasks;
using Avalonia.Controls;

namespace ApiConnectTool.Views;

/// <summary>Shared confirm/cancel wiring for the small modal dialogs in this folder (delete,
/// production-send) - each window keeps its own XAML and message text, but the confirm/cancel/
/// close-without-choosing plumbing is otherwise identical and previously duplicated per window.</summary>
internal static class ConfirmationDialogHelper
{
    public static Task<bool> ShowAsync(Window window, Window owner, Button confirmButton, Button cancelButton)
    {
        var tcs = new TaskCompletionSource<bool>();
        confirmButton.Click += (_, _) => { tcs.TrySetResult(true); window.Close(); };
        cancelButton.Click += (_, _) => { tcs.TrySetResult(false); window.Close(); };
        window.Closed += (_, _) => tcs.TrySetResult(false);

        window.ShowDialog(owner);
        return tcs.Task;
    }
}
