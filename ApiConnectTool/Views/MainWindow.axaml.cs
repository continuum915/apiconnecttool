using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views.Components;

namespace ApiConnectTool.Views
{
    public partial class MainWindow : Window, IAppDialogs
    {
        private bool _exitConfirmed;
        private bool _isConfirmingExit;

        public MainWindow()
        {
            InitializeComponent();
            KeyboardShortcuts.Register(this);
            DataContextChanged += (_, _) =>
            {
                if (ViewModel is not { } viewModel) return;
                viewModel.Commands.Dialogs = this;
                viewModel.VariablesPane.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(VariablesPaneViewModel.IsVisible)) ApplyVariablesPaneLayout(viewModel.VariablesPane.IsVisible);
                };
                ApplyVariablesPaneLayout(viewModel.VariablesPane.IsVisible);
            };
        }

        private Avalonia.Controls.GridLength _variablesPaneWidth = new(260);

        /// <summary>Collapses or restores the Variables pane columns, keeping the width the user
        /// dragged the splitter to.</summary>
        private void ApplyVariablesPaneLayout(bool visible)
        {
            var columns = BodyGrid.ColumnDefinitions;
            if (!visible && columns[4].Width.Value > 0) _variablesPaneWidth = columns[4].Width;
            columns[3].Width = new Avalonia.Controls.GridLength(visible ? 5 : 0);
            columns[4].Width = visible ? _variablesPaneWidth : new Avalonia.Controls.GridLength(0);
        }

        public void OpenCollectionRunner(ApiConnectTool.Models.RequestCollection collection, ApiConnectTool.Models.CollectionFolder? folder)
        {
            if (ViewModel is not { } viewModel) return;
            var runner = folder is null ? viewModel.CreateCollectionRunner(collection) : viewModel.CreateFolderRunner(folder);
            new CollectionRunnerWindow { DataContext = runner }.ShowDialog(this);
        }

        public void OpenCollectionSettings(ApiConnectTool.Models.RequestCollection collection)
        {
            if (ViewModel is { } viewModel)
                new CollectionSettingsWindow { DataContext = viewModel.CreateCollectionSettings(collection) }.ShowDialog(this);
        }

        void IAppDialogs.OpenEnvironments(ApiConnectTool.Models.RequestCollection? collection) => OpenEnvironments(collection);

        public Task ExportCollectionAsync(ApiConnectTool.Models.RequestCollection collection, bool asPostman) =>
            ViewModel is { } viewModel
                ? RequestActions.ExportCollectionAsync(this, viewModel.CollectionsPanel, collection, asPostman)
                : Task.CompletedTask;

        public Task CopyAsCurlAsync(RequestTabViewModel tab, bool revealSecrets) =>
            RequestActions.CopyAsCurlAsync(this, tab, revealSecrets);

        public void InspectVariables(RequestTabViewModel tab) => RequestActions.InspectVariables(this, tab);

        public void ShowComparison(ResponseComparisonViewModel comparison) =>
            new ResponseComparisonWindow { DataContext = comparison }.ShowDialog(this);

        void IAppDialogs.OpenHelp(string? topicId) => OpenHelp(topicId);

        /// <summary>Opens the Console window, or brings the open one to the front.</summary>
        public void OpenConsole()
        {
            if (ViewModel is not { } viewModel) return;
            if (OwnedWindows.OfType<ConsoleWindow>().FirstOrDefault() is { } existing)
            {
                existing.Activate();
                return;
            }
            new ConsoleWindow { DataContext = new ConsoleWindowViewModel(viewModel.ConsoleLog) }.Show(this);
        }

        /// <summary>Opens the History window, or brings the open one to the front.</summary>
        public void OpenHistoryWindow()
        {
            if (ViewModel is not { } viewModel) return;
            if (OwnedWindows.OfType<HistoryWindow>().FirstOrDefault() is { } existing)
            {
                existing.Activate();
                return;
            }
            new HistoryWindow { DataContext = new HistoryWindowViewModel(viewModel) }.Show(this);
        }

        internal MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

        public RequestEditorControl? ActiveRequestEditor =>
            this.GetVisualDescendants()
                .OfType<RequestEditorControl>()
                .FirstOrDefault(editor => editor.IsEffectivelyVisible);

        public void FocusUrlBox() => ActiveRequestEditor?.FocusUrlBox();

        /// <summary>Focuses the toolbar's collection environment picker and opens its list.</summary>
        public void OpenCollectionEnvironmentPicker()
        {
            if (this.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault(box => box.Name == "CollectionEnvironmentPicker") is not { IsEffectivelyEnabled: true } picker) return;
            picker.Focus();
            picker.IsDropDownOpen = true;
        }

        /// <summary>Opens the environment editor for <paramref name="collection"/>'s environments,
        /// or for the global environments when it is null.</summary>
        public EnvironmentEditorWindow? OpenEnvironments(ApiConnectTool.Models.RequestCollection? collection = null)
        {
            if (ViewModel is not { } viewModel) return null;
            if (OwnedWindows.OfType<EnvironmentEditorWindow>().FirstOrDefault(window =>
                    (window.DataContext as EnvironmentEditorViewModel)?.Collection == collection) is { } existing)
            {
                existing.Activate();
                return existing;
            }

            var window = new EnvironmentEditorWindow
            {
                DataContext = viewModel.CreateEnvironmentEditor(collection)
            };
            window.ShowDialog(this);
            return window;
        }

        // Closing can't await, so cancel this close, ask about unsaved tabs, and close again once
        // the user has saved or discarded them. _isConfirmingExit swallows repeat close clicks
        // while the prompt is already open.
        protected override async void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);
            if (e.Cancel || _exitConfirmed || ViewModel is not { } viewModel) return;

            e.Cancel = true;
            if (_isConfirmingExit) return;

            _isConfirmingExit = true;
            try
            {
                if (await viewModel.ConfirmExitAsync())
                {
                    await viewModel.History.FlushAsync(TimeSpan.FromSeconds(3));
                    _exitConfirmed = true;
                    Close();
                }
            }
            finally
            {
                _isConfirmingExit = false;
            }
        }

        /// <summary>Opens the import wizard, on the Format step or with a format preselected.</summary>
        public Task OpenImportWizardAsync(ApiConnectTool.Services.ImportFormat? format = null) =>
            ViewModel is { } viewModel ? ImportWizardWindow.ShowAsync(this, viewModel.CollectionsPanel, format) : Task.CompletedTask;

        private async void OnImportClick(object? sender, RoutedEventArgs e) => await OpenImportWizardAsync();
        private async void OnImportApiConnectToolCollectionClick(object? sender, RoutedEventArgs e) => await OpenImportWizardAsync(ApiConnectTool.Services.ImportFormat.ApiConnectTool);
        private async void OnImportPostmanCollectionClick(object? sender, RoutedEventArgs e) => await OpenImportWizardAsync(ApiConnectTool.Services.ImportFormat.Postman);
        private async void OnImportCurlClick(object? sender, RoutedEventArgs e) => await OpenImportWizardAsync(ApiConnectTool.Services.ImportFormat.Curl);
        private async void OnImportOpenApiClick(object? sender, RoutedEventArgs e) => await OpenImportWizardAsync(ApiConnectTool.Services.ImportFormat.OpenApi);

        private void OnExitClick(object? sender, RoutedEventArgs e)
        {
            // Close (not lifetime Shutdown) so exiting goes through the unsaved-changes prompt in
            // OnClosing; closing the main window ends the app.
            Close();
        }

        /// <summary>Edit > Environments... (and Ctrl+E): the active tab's collection, else the
        /// selected collection, else the global environments.</summary>
        public void OpenEnvironmentsForContext() => OpenEnvironments(ViewModel?.Commands.RunTarget);

        private void OnEnvironmentsClick(object? sender, RoutedEventArgs e) => OpenEnvironmentsForContext();

        private void OnGlobalEnvironmentsClick(object? sender, RoutedEventArgs e) => OpenEnvironments();

        private void OnPreferencesClick(object? sender, RoutedEventArgs e)
        {
            if (ViewModel is { } viewModel)
            {
                SettingsWindow.Show(this, viewModel);
            }
        }

        public HelpWindow OpenHelp(string? topicId = null) => HelpWindow.Show(this, topicId);

        private void OnHelpClick(object? sender, RoutedEventArgs e) => OpenHelp();

        private void OnAboutClick(object? sender, RoutedEventArgs e) => AboutWindow.Show(this);
    }
}
