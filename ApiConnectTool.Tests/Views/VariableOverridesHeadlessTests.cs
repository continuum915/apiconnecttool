using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views.Components;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class VariableOverridesHeadlessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TabReachesEditableKeySecretAndResetAndColumnsCanResize(bool dark) => HeadlessSession.RunAsync(() =>
    {
        var row = new EnvironmentVariable { Key = "key", InitialValue = "default", CurrentValue = "override" };
        var control = new VariableEditorControl { DataContext = new VariableEditorViewModel(new() { row }, () => { }) };
        var window = new Window { Content = control, Width = 600, Height = 500,
            RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light };
        try
        {
            window.Show(); window.UpdateLayout();
            var visited = new HashSet<Control>();
            control.GetVisualDescendants().OfType<Button>().First().Focus();
            for (var i = 0; i < 30; i++)
            {
                window.KeyPress(Key.Tab, Avalonia.Input.RawInputModifiers.None, PhysicalKey.Tab, null); window.KeyRelease(Key.Tab, Avalonia.Input.RawInputModifiers.None, PhysicalKey.Tab, null);
                if (window.FocusManager!.GetFocusedElement() is Control focused) visited.Add(focused);
            }
            Assert.Contains(visited, x => x is TextBox box && box.Text == "key");
            Assert.Contains(visited, x => x is CheckBox box && Equals(Avalonia.Automation.AutomationProperties.GetName(box), "Secret"));
            Assert.Contains(visited, x => x is Button button && Equals(button.Content, "Reset override"));
            var secret = visited.OfType<CheckBox>().Single(x => Equals(Avalonia.Automation.AutomationProperties.GetName(x), "Secret"));
            secret.Focus();
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.True(row.IsSecret);
            var reset = visited.OfType<Button>().Single(x => Equals(x.Content, "Reset override"));
            reset.Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.False(row.UseOverride);
            Assert.False(reset.IsEnabled);
            var grid = control.GetVisualDescendants().OfType<DataGrid>().Single();
            Assert.True(grid.CanUserResizeColumns);
            Assert.All(grid.Columns, column => Assert.True(column.ActualWidth >= 80));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task EmptyOverrideCanBeEnabledAndResetWithoutDeletingDefinition() => HeadlessSession.RunAsync(() =>
    {
        var row = new EnvironmentVariable { Key = "token", InitialValue = "default", IsSecret = true };
        var id = row.Id;
        var control = new VariableEditorControl { DataContext = new VariableEditorViewModel(new() { row }, () => { }) };
        var window = new Window { Content = control, Width = 1100, Height = 500 };
        try
        {
            window.Show(); window.UpdateLayout();
            Assert.False(row.HasCurrentValue);
            var use = control.GetVisualDescendants().OfType<CheckBox>().Single(x => Equals(Avalonia.Automation.AutomationProperties.GetName(x), "Use override"));
            var reset = control.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "Reset override"));
            Assert.False(reset.IsEnabled);
            use.IsChecked = true;
            Assert.True(row.HasCurrentValue);
            Assert.Equal("", new RequestEnvironment { Variables = new() { row } }.BuildVariableMap()["token"]);
            reset.Command!.Execute(reset.CommandParameter);
            Assert.False(row.HasCurrentValue);
            Assert.Equal("", row.CurrentValue.Value);
            Assert.Equal("default", row.InitialValue.Value);
            Assert.Equal(id, row.Id);
            Assert.True(row.IsEnabled);
            Assert.True(row.IsSecret);
        }
        finally { window.Close(); }
    });
}
