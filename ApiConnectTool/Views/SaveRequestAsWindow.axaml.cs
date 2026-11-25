using System.Threading.Tasks;
using Avalonia.Controls;
using ApiConnectTool.Services.Localization;
using ApiConnectTool.ViewModels;

namespace ApiConnectTool.Views;

public partial class SaveRequestAsWindow : Window
{
    public SaveRequestAsWindow()
    {
        InitializeComponent();
    }

    /// <summary>Builds the dialog for <paramref name="request"/> without showing it. Separate
    /// from <see cref="ShowAsync"/> so headless tests can inspect what it renders.</summary>
    public static SaveRequestAsWindow Create(SaveAsPromptRequest request)
    {
        var window = new SaveRequestAsWindow();
        window.NameTextBox.Text = request.DefaultName;
        window.NameTextBox.SelectAll();
        window.SkipExplanationText.Text =
            string.Format(AppLocalization.Get("Str.SaveRequestAs.SkipExplanation"), request.DefaultName);

        // The first save of a new request always asks (there's no "don't show again" for it),
        // and Duplicate always lands next to the original (no location to choose).
        if (request.Mode == SaveAsPromptMode.SaveNew)
        {
            window.Title = AppLocalization.Get("Str.SaveRequest.Title");
            window.DescriptionText.Text = AppLocalization.Get("Str.SaveRequest.Description");
            window.DontShowAgainCheckBox.IsVisible = false;
            window.SkipExplanationText.IsVisible = false;
        }

        window.LocationPanel.IsVisible = request.Mode != SaveAsPromptMode.Duplicate && request.Destinations.Count > 0;
        window.LocationList.ItemsSource = request.Destinations;
        window.LocationList.SelectedItem = request.DefaultDestination;
        return window;
    }

    public static Task<SaveAsPromptResult?> ShowAsync(Window owner, SaveAsPromptRequest request)
    {
        var window = Create(request);
        if (request.DefaultDestination is { } defaultDestination)
        {
            window.Opened += (_, _) => window.LocationList.ScrollIntoView(defaultDestination);
        }

        var tcs = new TaskCompletionSource<SaveAsPromptResult?>();
        window.SaveButton.Click += (_, _) =>
        {
            tcs.TrySetResult(new SaveAsPromptResult(
                window.NameTextBox.Text ?? request.DefaultName,
                window.DontShowAgainCheckBox.IsChecked == true,
                window.LocationList.SelectedItem as SaveDestination));
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
