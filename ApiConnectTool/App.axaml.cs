using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using ApiConnectTool.Services.Localization;
using ApiConnectTool.ViewModels;
using ApiConnectTool.Views;
using System;

namespace ApiConnectTool
{
    public partial class App : Application
    {
        public static IServiceProvider? Services { get; private set; }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            AppLocalization.Apply(AppLocalization.DefaultCulture);
            HelpContext.EnsureInitialized();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                Services = Program.BuildServiceProvider();
                var viewModel = Services.GetRequiredService<MainWindowViewModel>();
                var themeManager = new ApiConnectTool.Services.AppThemeManager(this);
                themeManager.Apply(viewModel.Theme);
                viewModel.ThemeChangedHandler = themeManager.Apply;

                var mainWindow = new MainWindow { DataContext = viewModel };
                viewModel.ProductionSendConfirmationHandler = targets =>
                    Views.ProductionSendConfirmationWindow.ShowAsync(mainWindow, targets);
                viewModel.SaveAsPromptHandler = request =>
                    Views.SaveRequestAsWindow.ShowAsync(mainWindow, request);
                viewModel.CollectionsPanel.MoveDestinationPromptHandler = (request, destinations, current) =>
                    Views.MoveRequestWindow.ShowAsync(mainWindow, request, destinations, current);
                viewModel.UnsavedChangesPromptHandler = dirtyTabs =>
                    Views.UnsavedChangesWindow.ShowAsync(mainWindow, dirtyTabs);
                viewModel.ExitWithUnsavedWorkspacePromptHandler = failureDetail =>
                    Views.UnsavedChangesWindow.ShowExitWithUnsavedWorkspaceAsync(mainWindow, failureDetail);

                desktop.MainWindow = mainWindow;
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
