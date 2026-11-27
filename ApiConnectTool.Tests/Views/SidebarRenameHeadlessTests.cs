using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ApiConnectTool.Models;
using ApiConnectTool.ViewModels;
using Xunit;

namespace ApiConnectTool.Tests.Views;

/// <summary>Inline rename in the real sidebar: the box takes focus with its whole name selected,
/// and new folders and collections open straight into it.</summary>
public class SidebarRenameHeadlessTests
{
    private static TextBox FocusedRenameBox(SidebarHeadlessHarness harness)
    {
        Dispatcher.UIThread.RunJobs();
        harness.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var box = harness.Sidebar.GetVisualDescendants().OfType<TextBox>()
            .Single(candidate => candidate.Classes.Contains("rename") && candidate.IsEffectivelyVisible);
        Assert.True(box.IsFocused, "the rename box should have keyboard focus");
        Assert.Equal(0, box.SelectionStart);
        Assert.Equal(box.Text!.Length, box.SelectionEnd);
        return box;
    }

    [Fact]
    public Task RenamingAFolderSelectsItsWholeName() => HeadlessSession.RunAsync(() =>
    {
        using var harness = new SidebarHeadlessHarness();
        var folder = harness.MainWindow.CollectionsPanel.Collections.Single().Folders.Single();

        folder.RenameOriginalName = folder.Name;
        folder.IsEditing = true;

        Assert.Equal("Orders", FocusedRenameBox(harness).Text);
    });

    [Fact]
    public Task AddingAFolderOpensItInRenameWithTheNameSelected() => HeadlessSession.RunAsync(() =>
    {
        using var harness = new SidebarHeadlessHarness();
        var panel = harness.MainWindow.CollectionsPanel;

        panel.AddFolderCommand.Execute(panel.Collections.Single());

        Assert.Equal("New Folder", FocusedRenameBox(harness).Text);
        Assert.Equal("New Folder", Assert.IsType<FolderTreeNode>(panel.SelectedNode).Folder.Name);
    });

    [Fact]
    public Task AddingACollectionOpensItInRenameWithTheNameSelected() => HeadlessSession.RunAsync(() =>
    {
        using var harness = new SidebarHeadlessHarness();
        var panel = harness.MainWindow.CollectionsPanel;

        panel.AddCollectionCommand.Execute(null);

        Assert.Equal("New Collection", FocusedRenameBox(harness).Text);
        Assert.Equal("New Collection", Assert.IsType<CollectionTreeNode>(panel.SelectedNode).Collection.Name);
    });

    [Fact]
    public Task AddingARequestToACollapsedFolderExpandsAndSelectsIt() => HeadlessSession.RunAsync(() =>
    {
        using var harness = new SidebarHeadlessHarness();
        var panel = harness.MainWindow.CollectionsPanel;
        var folder = panel.Collections.Single().Folders.Single();

        panel.AddRequestToFolderCommand.Execute(folder);
        Dispatcher.UIThread.RunJobs();
        harness.Window.UpdateLayout();

        var added = Assert.IsType<RequestItem>(panel.SelectedNode);
        Assert.Equal(folder.Id, added.FolderId);
        Assert.True(harness.Folder("Orders").IsExpanded);
        Assert.True(harness.Request(added.Name).IsEffectivelyVisible);
    });
}
