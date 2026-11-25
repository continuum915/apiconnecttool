using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;
using ApiConnectTool.ViewModels;

namespace ApiConnectTool.Views;

public partial class MoveRequestWindow : Window
{
    public MoveRequestWindow() => InitializeComponent();

    /// <summary>Builds the dialog for <paramref name="request"/>, listing every destination with
    /// its current one selected. Separate from <see cref="ShowAsync"/> so headless tests can
    /// inspect what it renders.</summary>
    public static MoveRequestWindow Create(RequestItem request, IReadOnlyList<SaveDestination> destinations, SaveDestination? current)
    {
        var window = new MoveRequestWindow();
        window.MessageText.Text = string.Format(AppLocalization.Get("Str.MoveRequest.Message"), request.Name);
        window.LocationList.ItemsSource = destinations;
        window.LocationList.SelectedItem = current;
        return window;
    }

    /// <summary>Asks which collection/folder to move <paramref name="request"/> to, starting on its
    /// current location. Returns null if cancelled or closed.</summary>
    public static Task<SaveDestination?> ShowAsync(
        Window owner,
        RequestItem request,
        IReadOnlyList<SaveDestination> destinations,
        SaveDestination? current)
    {
        var window = Create(request, destinations, current);
        if (current is not null)
        {
            window.Opened += (_, _) => window.LocationList.ScrollIntoView(current);
        }

        var tcs = new TaskCompletionSource<SaveDestination?>();
        window.MoveButton.Click += (_, _) =>
        {
            tcs.TrySetResult(window.LocationList.SelectedItem as SaveDestination);
            window.Close();
        };
        window.CancelButton.Click += (_, _) =>
        {
            tcs.TrySetResult(null);
            window.Close();
        };
        window.Closed += (_, _) => tcs.TrySetResult(null);

        window.ShowDialog(owner);
        return tcs.Task;
    }
}
