using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class ResetAllOverridesHeadlessTests
{
    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Win7Light)]
    public Task ResetAllRequiresConfirmation(AppTheme theme) => HeadlessSession.RunAsync(() =>
    {
        var app = Avalonia.Application.Current!;
        var previousTheme = app.RequestedThemeVariant;
        var manager = new ApiConnectTool.Services.AppThemeManager(app);
        manager.Apply(theme);
        var row = new EnvironmentVariable { Key = "key", InitialValue = "default", CurrentValue = "legacy" };
        var editor = new VariableEditorViewModel(new() { row }, () => { });
        var window = new Window { Content = new ApiConnectTool.Views.Components.VariableEditorControl { DataContext = editor }, Width = 1100, Height = 500 };
        try
        {
            window.Show(); window.UpdateLayout();
            var reset = window.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "Reset all overrides..."));
            reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var dialog = Assert.Single(window.OwnedWindows);
            var cancel = dialog.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "Cancel"));
            Assert.Same(cancel, dialog.FocusManager!.GetFocusedElement());
            dialog.KeyPress(Avalonia.Input.Key.Tab, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Tab, null);
            dialog.KeyRelease(Avalonia.Input.Key.Tab, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Tab, null);
            Assert.Equal("Reset all overrides", Assert.IsType<Button>(dialog.FocusManager.GetFocusedElement()).Content);
            Assert.Equal("legacy", row.CurrentValue.Value);
            dialog.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "Cancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("legacy", row.CurrentValue.Value);
            reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dialog = Assert.Single(window.OwnedWindows);
            dialog.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "Reset all overrides")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(row.UseOverride);
            Assert.Equal("default", row.InitialValue.Value);
            Assert.False(reset.IsEnabled);
            row.HasCurrentValue = true;
            Assert.True(reset.IsEnabled);
            row.UseOverride = false;
            Assert.False(reset.IsEnabled);
            editor.Variables.Add(new() { CurrentValue = "legacy" });
            Assert.True(reset.IsEnabled);
            editor.Variables.Clear();
            Assert.False(reset.IsEnabled);
        }
        finally { window.Close(); editor.Dispose(); manager.Apply(AppTheme.Light); app.RequestedThemeVariant = previousTheme; }
    });
}
