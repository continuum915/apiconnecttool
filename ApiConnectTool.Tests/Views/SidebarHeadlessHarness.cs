using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ApiConnectTool.Models;
using ApiConnectTool.Tests.ViewModels;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views.Components;

namespace ApiConnectTool.Tests.Views;

/// <summary>The real themed sidebar at a chosen width. Use on the shared
/// headless session's UI thread; disposing closes the window even when an assertion fails.</summary>
internal sealed class SidebarHeadlessHarness : IDisposable
{
    public SidebarHeadlessHarness(Action<RequestCollection>? arrange = null, double width = 260)
    {
        var folder = new CollectionFolder { Name = "Orders" };
        var collection = new RequestCollection { Name = "Sample API" };
        collection.Folders.Add(folder);
        collection.Requests.Add(new RequestItem { Name = "List orders", FolderId = folder.Id });
        arrange?.Invoke(collection);
        var service = new FakeWorkspaceService
        {
            WorkspaceToReturn = new Workspace { Collections = new ObservableCollection<RequestCollection> { collection } }
        };
        MainWindow = new MainWindowViewModel(service);
        Sidebar = new SidebarControl();
        Window = new Window
        {
            Width = width,
            Height = 900,
            DataContext = MainWindow,
            Content = Sidebar
        };
        Window.Show();
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
    }

    public MainWindowViewModel MainWindow { get; }
    public SidebarControl Sidebar { get; }
    public Window Window { get; }
    public TreeView Tree => Sidebar.FindControl<TreeView>("NavigatorTree")!;

    public TreeViewItem Item(Func<object?, bool> match) => Sidebar.GetVisualDescendants().OfType<TreeViewItem>()
        .Single(item => match(item.DataContext));

    public TreeViewItem Folder(string name) => Item(context => context is FolderTreeNode node && node.Folder.Name == name);

    public TreeViewItem Collection() => Item(context => context is CollectionTreeNode);

    public TreeViewItem Request(string name) => Item(context => context is RequestItem request && request.Name == name);

    /// <summary>The row (the item's own header content) that owns the context menu.</summary>
    public static Grid Row(TreeViewItem item) => item.GetVisualDescendants().OfType<Grid>()
        .First(grid => grid.Classes.Contains("nav-row") && ReferenceEquals(grid.DataContext, item.DataContext));

    public void Dispose() => Window.Close();
}
