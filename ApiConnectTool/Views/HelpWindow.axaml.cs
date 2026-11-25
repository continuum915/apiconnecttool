using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using ApiConnectTool.Help;
using ApiConnectTool.ViewModels;

namespace ApiConnectTool.Views;

public partial class HelpWindow : Window
{
    private static HelpWindow? _activeWindow;
    private HelpViewModel? _subscribedViewModel;

    public HelpWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    internal HelpViewModel? ViewModel => DataContext as HelpViewModel;

    public int ActivationCount { get; private set; }

    public static HelpWindow Show(Window owner, string? topicId = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var existing = FindExistingHelpWindow(owner);
        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(topicId))
            {
                existing.ViewModel?.NavigateTo(topicId);
            }

            existing.BringToFront();
            return existing;
        }

        var window = new HelpWindow
        {
            DataContext = new HelpViewModel(HelpTopicCatalog.Load(), topicId)
        };
        _activeWindow = window;
        ((Window)window).Show(owner);
        window.BringToFront();
        return window;
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Focus();
        ActivationCount++;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (ReferenceEquals(_activeWindow, this))
        {
            _activeWindow = null;
        }

        DetachViewModel();
        base.OnClosed(e);
    }

    private static HelpWindow? FindExistingHelpWindow(Window owner)
    {
        if (_activeWindow is { IsVisible: true })
        {
            return _activeWindow;
        }

        for (Window? current = owner; current is not null; current = current.Owner as Window)
        {
            if (current.OwnedWindows.OfType<HelpWindow>().FirstOrDefault(window => window.IsVisible) is { } owned)
            {
                return owned;
            }
        }

        return null;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        DetachViewModel();
        if (DataContext is HelpViewModel viewModel)
        {
            _subscribedViewModel = viewModel;
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        RenderCurrentTopic();
    }

    private void DetachViewModel()
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HelpViewModel.SelectedTopic))
        {
            RenderCurrentTopic();
        }
    }

    private void RenderCurrentTopic()
    {
        if (ViewModel is { SelectedTopic: { } topic } viewModel)
        {
            ArticleHost.Content = HelpMarkdownRenderer.Render(
                topic.BodyMarkdown,
                id => viewModel.NavigateTo(id),
                viewModel.CanResolveTopic);
            ArticleScrollViewer.Offset = Vector.Zero;
        }
        else
        {
            ArticleHost.Content = null;
        }
    }
}
