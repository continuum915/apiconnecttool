using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using ApiConnectTool.Services.Localization;
using ApiConnectTool.ViewModels;

namespace ApiConnectTool.Views;

public partial class UnsavedChangesWindow : Window
{
    public UnsavedChangesWindow() => InitializeComponent();

    /// <summary>Asks Save / Don't Save / Cancel for <paramref name="dirtyTabs"/>. Closing the
    /// window without choosing counts as Cancel, so nothing is discarded by accident.</summary>
    public static Task<UnsavedChangesChoice> ShowAsync(Window owner, IReadOnlyList<RequestTabViewModel> dirtyTabs)
    {
        var window = new UnsavedChangesWindow();
        window.MessageText.Text = dirtyTabs.Count == 1
            ? string.Format(AppLocalization.Get("Str.UnsavedChanges.SingleMessage"), dirtyTabs[0].Name)
            : string.Format(AppLocalization.Get("Str.UnsavedChanges.MultipleMessage"), dirtyTabs.Count);

        var tcs = new TaskCompletionSource<UnsavedChangesChoice>();
        window.SaveButton.Click += (_, _) => { tcs.TrySetResult(UnsavedChangesChoice.Save); window.Close(); };
        window.DontSaveButton.Click += (_, _) => { tcs.TrySetResult(UnsavedChangesChoice.Discard); window.Close(); };
        window.CancelButton.Click += (_, _) => { tcs.TrySetResult(UnsavedChangesChoice.Cancel); window.Close(); };
        window.Closed += (_, _) => tcs.TrySetResult(UnsavedChangesChoice.Cancel);

        window.ShowDialog(owner);
        return tcs.Task;
    }

    /// <summary>Asked on exit when the workspace still can't be written to disk. Same layout
    /// with no Save button (saving just failed): returns true only for "Exit Without Saving".</summary>
    public static Task<bool> ShowExitWithUnsavedWorkspaceAsync(Window owner, string failureDetail)
    {
        var window = new UnsavedChangesWindow();
        window.Title = AppLocalization.Get("Str.WorkspaceNotSaved.Title");
        window.MessageText.Text = string.Format(AppLocalization.Get("Str.WorkspaceNotSaved.Message"), failureDetail);
        window.SaveButton.IsVisible = false;
        window.CancelButton.IsDefault = true;
        window.DontSaveButton.Content = AppLocalization.Get("Str.WorkspaceNotSaved.ExitAnyway");
        return ConfirmationDialogHelper.ShowAsync(window, owner, window.DontSaveButton, window.CancelButton);
    }
}
