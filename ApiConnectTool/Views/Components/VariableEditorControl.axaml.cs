using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;
using ApiConnectTool.ViewModels;
namespace ApiConnectTool.Views.Components;
public partial class VariableEditorControl : UserControl
{
    public VariableEditorControl() => InitializeComponent();
    private void OnAddVariableClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VariableEditorViewModel editor) return;
        editor.AddVariableCommand.Execute(null);
        var row = editor.Variables.Last();
        VariableGrid.SelectedItem = row;
        VariableGrid.ScrollIntoView(row, VariableGrid.Columns[1]);
        Dispatcher.UIThread.Post(() =>
        {
            VariableGrid.UpdateLayout();
            var key = VariableGrid.GetVisualDescendants().OfType<TextBox>()
                .FirstOrDefault(box => box.Name == "VariableKeyTextBox" && ReferenceEquals(box.DataContext, row));
            key?.Focus();
        }, DispatcherPriority.Loaded);
    }
    private async void OnResetAllClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VariableEditorViewModel editor || !editor.HasOverrides || TopLevel.GetTopLevel(this) is not Window owner) return;
        if (await ResetOverridesConfirmationWindow.ShowAsync(owner, editor.ScopeName, editor.OverrideCount))
            editor.ResetAllOverridesCommand.Execute(null);
    }
}
