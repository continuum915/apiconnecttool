using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;
using ApiConnectTool.ViewModels;

namespace ApiConnectTool.Views.Components
{
    /// <summary>The navigator. Context-menu items read their target from the item's Tag and call
    /// the same collections-panel commands the Collection and Request menus use; keyboard
    /// shortcuts (F2, Delete, Enter, Shift+F10, the Menu key) act on the selected node.</summary>
    public partial class SidebarControl : UserControl
    {
        public SidebarControl()
        {
            InitializeComponent();
            DataContextChanged += (_, _) =>
            {
                if (DataContext is MainWindowViewModel viewModel)
                {
                    viewModel.CollectionsPanel.DeleteConfirmationHandler = ConfirmDeleteAsync;
                }
            };
            NavigatorTree.AddHandler(KeyDownEvent, OnTreeKeyDown, RoutingStrategies.Tunnel);
            HistoryList.AddHandler(KeyDownEvent, OnHistoryKeyDown, RoutingStrategies.Tunnel);
            SearchResultsList.AddHandler(KeyDownEvent, OnSearchResultKeyDown, RoutingStrategies.Tunnel);
        }

        private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;
        private CollectionsViewModel? Panel => ViewModel?.CollectionsPanel;
        private IAppDialogs? Dialogs => ViewModel?.Commands.Dialogs ?? TopLevel.GetTopLevel(this) as IAppDialogs;

        private Task<bool> ConfirmDeleteAsync(CollectionItemKind kind, string displayName)
        {
            var owner = this.FindAncestorOfType<Window>();
            if (owner is null) return Task.FromResult(false);

            var description = string.Format(
                AppLocalization.Get($"Str.Sidebar.Delete{kind}Description"), displayName);
            return DeleteConfirmationWindow.ShowAsync(owner, description);
        }

        // Keyboard

        private void OnTreeKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Source is TextBox || (e.Source as Visual)?.FindAncestorOfType<TextBox>() is not null) return;
            if (Panel is not { } panel || panel.SelectedNode is not { } node) return;

            switch (e.Key)
            {
                case Key.F2:
                    BeginRename(node);
                    e.Handled = true;
                    break;
                case Key.Delete:
                    _ = DeleteAsync(panel, node);
                    e.Handled = true;
                    break;
                case Key.Enter when node is RequestItem request:
                    ViewModel?.OpenRequestCommand.Execute(request);
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>Shift+F10 and the Menu key raise ContextRequested on the focused tree item,
        /// which is the row's container rather than the row that owns the menu.</summary>
        private void OnTreeContextRequested(object? sender, ContextRequestedEventArgs e)
        {
            if (e.Source is not TreeViewItem item) return;
            var row = item.GetVisualDescendants().OfType<Control>()
                .FirstOrDefault(control => control.ContextMenu is not null && ReferenceEquals(control.DataContext, item.DataContext));
            if (row?.ContextMenu is null) return;
            row.ContextMenu.Open(row);
            e.Handled = true;
        }

        private static void BeginRename(object node)
        {
            IRenamable? renamable = node switch
            {
                CollectionTreeNode collection => collection.Collection,
                FolderTreeNode folder => folder.Folder,
                IRenamable item => item,
                _ => null
            };
            if (renamable is null) return;
            renamable.RenameOriginalName = renamable.Name;
            renamable.IsEditing = true;
        }

        private static Task DeleteAsync(CollectionsViewModel panel, object node) => node switch
        {
            CollectionTreeNode collection => panel.DeleteCollectionCommand.ExecuteAsync(collection.Collection),
            FolderTreeNode folder => panel.DeleteFolderCommand.ExecuteAsync(folder.Folder),
            RequestItem request => panel.DeleteRequestCommand.ExecuteAsync(request),
            _ => Task.CompletedTask
        };

        // Collection context menu

        private void OnAddRequestClick(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: RequestCollection collection }) Panel?.AddRequestCommand.Execute(collection);
        }

        private void OnAddFolderClick(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: RequestCollection collection }) Panel?.AddFolderCommand.Execute(collection);
        }

        private void OnRenameCollectionClick(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: RequestCollection collection }) BeginRename(collection);
        }

        private void OnDuplicateCollectionClick(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: RequestCollection collection }) Panel?.DuplicateCollectionCommand.Execute(collection);
        }

        private void OnRunCollectionClick(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: RequestCollection collection }) Dialogs?.OpenCollectionRunner(collection, null);
        }

        private void OnCollectionEnvironmentsClick(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: RequestCollection collection }) Dialogs?.OpenEnvironments(collection);
        }

        private void OnCollectionSettingsClick(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: RequestCollection collection }) Dialogs?.OpenCollectionSettings(collection);
        }

        private async void OnExportCollectionClick(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: RequestCollection collection } && Dialogs is { } dialogs)
                await dialogs.ExportCollectionAsync(collection, asPostman: false);
        }

        private async void OnExportCollectionAsPostmanClick(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: RequestCollection collection } && Dialogs is { } dialogs)
                await dialogs.ExportCollectionAsync(collection, asPostman: true);
        }

        private async void OnDeleteCollectionClick(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: RequestCollection collection } && Panel is { } panel)
                await panel.DeleteCollectionCommand.ExecuteAsync(collection);
        }

        // Folder context menu

        private void OnRenameFolderClick(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: CollectionFolder folder }) BeginRename(folder);
        }

        private void OnRunFolderClick(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: CollectionFolder folder } &&
                Panel?.Collections.FirstOrDefault(collection => collection.Folders.Contains(folder)) is { } collection)
                Dialogs?.OpenCollectionRunner(collection, folder);
        }

        private void OnFolderSettingsClick(object? sender, RoutedEventArgs e)
        {
            if (ViewModel is not { } viewModel || sender is not MenuItem { Tag: CollectionFolder folder }
                || TopLevel.GetTopLevel(this) is not Window owner) return;
            new FolderSettingsWindow { DataContext = viewModel.CreateFolderSettings(folder) }.ShowDialog(owner);
        }

        private async void OnDeleteFolderClick(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: CollectionFolder folder } && Panel is { } panel)
                await panel.DeleteFolderCommand.ExecuteAsync(folder);
        }

        // Request context menu

        private void OnOpenRequestClick(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: RequestItem request }) ViewModel?.OpenRequestCommand.Execute(request);
        }

        private void OnRenameRequestClick(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: RequestItem request }) BeginRename(request);
        }

        private void OnDuplicateRequestClick(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: RequestItem request }) Panel?.DuplicateRequestCommand.Execute(request);
        }

        private void OnMoveRequestToClick(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: RequestItem request }) Panel?.MoveRequestToCommand.Execute(request);
        }

        private void OnMoveRequestUpClick(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: RequestItem request }) Panel?.MoveRequestUpCommand.Execute(request);
        }

        private void OnMoveRequestDownClick(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: RequestItem request }) Panel?.MoveRequestDownCommand.Execute(request);
        }

        private async void OnDeleteRequestClick(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: RequestItem request } && Panel is { } panel)
                await panel.DeleteRequestCommand.ExecuteAsync(request);
        }

        private void OnRequestRowPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control { DataContext: RequestItem request } row ||
                e.Source is Control source && (source is TextBox || source.FindAncestorOfType<TextBox>() is not null) ||
                !e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;

            if (Panel is { } panel) panel.SelectedNode = request;
            ViewModel?.OpenRequestCommand.Execute(request);
        }

        // Inline rename. Two-way binding already writes keystrokes into the model; on blur the
        // Rename*Command trims, falls back to a default name if left blank, and persists.

        private void OnCollectionNameLostFocus(object? sender, RoutedEventArgs e)
        {
            if (sender is TextBox { DataContext: CollectionTreeNode node })
            {
                Panel?.RenameCollectionCommand.Execute(node.Collection);
                node.Collection.IsEditing = false;
            }
        }

        private void OnFolderNameLostFocus(object? sender, RoutedEventArgs e)
        {
            if (sender is TextBox { DataContext: FolderTreeNode node })
            {
                Panel?.RenameFolderCommand.Execute(node.Folder);
                node.Folder.IsEditing = false;
            }
        }

        private void OnRequestNameLostFocus(object? sender, RoutedEventArgs e)
        {
            if (sender is TextBox { DataContext: RequestItem request })
            {
                Panel?.RenameRequestCommand.Execute(request);
                request.IsEditing = false;
            }
        }

        private void OnRenameKeyDown(object? sender, KeyEventArgs e)
        {
            if (sender is not TextBox { DataContext: object item }) return;

            if (e.Key == Key.Escape)
            {
                // Cancel semantics belong next to the commit path (Rename*Command), so the view
                // model reverts the name.
                IRenamable? renamable = item switch
                {
                    CollectionTreeNode collectionNode => collectionNode.Collection,
                    FolderTreeNode folderNode => folderNode.Folder,
                    RequestItem request => request,
                    _ => null
                };
                Panel?.CancelRenameCommand.Execute(renamable);
                e.Handled = true;
                return;
            }

            if (e.Key != Key.Enter) return;

            switch (item)
            {
                case CollectionTreeNode collectionNode:
                    Panel?.RenameCollectionCommand.Execute(collectionNode.Collection);
                    collectionNode.Collection.IsEditing = false;
                    break;
                case FolderTreeNode folderNode:
                    Panel?.RenameFolderCommand.Execute(folderNode.Folder);
                    folderNode.Folder.IsEditing = false;
                    break;
                case RequestItem request:
                    Panel?.RenameRequestCommand.Execute(request);
                    request.IsEditing = false;
                    break;
            }

            e.Handled = true;
        }

        // Search results

        private void OnSearchResultPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Control { DataContext: RequestItem request } row && e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
                ViewModel?.OpenRequestCommand.Execute(request);
        }

        private void OnSearchResultKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && SearchResultsList.SelectedItem is RequestItem request)
            {
                ViewModel?.OpenRequestCommand.Execute(request);
                e.Handled = true;
            }
        }

        // History

        private void OnHistoryDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (HistoryList.SelectedItem is HistoryEntry entry) ViewModel?.History.ResendCommand.Execute(entry);
        }

        private void OnHistoryKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && HistoryList.SelectedItem is HistoryEntry entry)
            {
                ViewModel?.History.ResendCommand.Execute(entry);
                e.Handled = true;
            }
        }

        private void OnHistoryResendClick(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: HistoryEntry entry }) ViewModel?.History.ResendCommand.Execute(entry);
        }

        private void OnCompareHistoryEntriesClick(object? sender, RoutedEventArgs e)
        {
            if (ViewModel is { } viewModel && sender is MenuItem { Tag: HistoryEntry entry } &&
                viewModel.TryCompareHistoryEntries(entry) is { } comparison)
                Dialogs?.ShowComparison(comparison);
        }

        private void OnCompareWithCurrentClick(object? sender, RoutedEventArgs e)
        {
            if (ViewModel is { } viewModel && sender is MenuItem { Tag: HistoryEntry entry } &&
                viewModel.CompareHistoryEntryWithCurrentTab(entry) is { } comparison)
                Dialogs?.ShowComparison(comparison);
        }
    }
}
