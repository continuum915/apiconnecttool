using System;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.ViewModels;

/// <summary>The main window's status bar: the latest transient message (for a few seconds,
/// otherwise "Ready") and the active context: collection, environment, layout, proxy and the
/// workspace save state.</summary>
public sealed partial class StatusBarViewModel : ObservableObject
{
    /// <summary>How long a transient message stays before the bar returns to "Ready".</summary>
    public static readonly TimeSpan MessageDuration = TimeSpan.FromSeconds(5);

    private readonly MainWindowViewModel _main;
    private readonly Func<TimeSpan, Action, IDisposable> _schedule;
    private IDisposable? _expiry;
    private RequestTabViewModel? _observedTab;

    [ObservableProperty] private string _message = Text("Str.Status.Ready");
    [ObservableProperty] private string _collectionText = string.Empty;
    [ObservableProperty] private string _environmentText = string.Empty;
    [ObservableProperty] private bool _isProductionEnvironment;
    [ObservableProperty] private string _layoutText = string.Empty;
    [ObservableProperty] private string _proxyText = string.Empty;
    [ObservableProperty] private string _saveStateText = string.Empty;
    [ObservableProperty] private bool _hasSaveError;

    /// <param name="schedule">Runs an action once after a delay and returns a handle that
    /// cancels it; the default uses the UI dispatcher. Tests pass a manual scheduler.</param>
    public StatusBarViewModel(MainWindowViewModel main, Func<TimeSpan, Action, IDisposable>? schedule = null)
    {
        _main = main;
        _schedule = schedule ?? ScheduleOnCurrentContext;
        main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.ActiveTab)) ObserveTab(main.ActiveTab);
            if (e.PropertyName is nameof(MainWindowViewModel.ActiveTab) or nameof(MainWindowViewModel.SelectedEnvironment)
                or nameof(MainWindowViewModel.IsVerticalLayout) or nameof(MainWindowViewModel.WorkspaceSaveError))
                Refresh();
        };
        main.CollectionsPanel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CollectionsViewModel.ImportExportMessage) && main.CollectionsPanel.ImportExportMessage is { } message)
                Post(message);
        };
        main.CollectionEnvironmentPicker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CollectionEnvironmentPickerViewModel.SelectedChoice)) Refresh();
        };
        ObserveTab(main.ActiveTab);
        Refresh();
    }

    /// <summary>Shows <paramref name="message"/> until <see cref="MessageDuration"/> passes or
    /// another message replaces it.</summary>
    public void Post(string message)
    {
        _expiry?.Dispose();
        Message = message;
        _expiry = _schedule(MessageDuration, () =>
        {
            _expiry = null;
            Message = Text("Str.Status.Ready");
        });
    }

    /// <summary>Rereads the context fields. The main window calls it after workspace saves,
    /// which is how collection environment switches arrive.</summary>
    public void Refresh()
    {
        var tab = _main.ActiveTab;
        var collection = tab?.CollectionProvider?.Invoke();
        CollectionText = tab is null ? Text("Str.Status.NoRequest")
            : collection?.Name ?? Text("Str.Status.UnsavedRequest");

        var environment = collection?.ActiveEnvironment ?? _main.SelectedEnvironment;
        IsProductionEnvironment = environment?.IsProduction == true;
        EnvironmentText = environment is null ? Text("Str.Status.NoEnvironment") : EnvironmentChoice.Label(environment);

        LayoutText = Text(_main.IsVerticalLayout ? "Str.Status.LayoutVertical" : "Str.Status.LayoutHorizontal");
        ProxyText = string.Format(CultureInfo.CurrentCulture, Text("Str.Status.Proxy"), ProxyHost(tab?.ProxyUrl) ?? Text("Str.Status.NoProxy"));
        HasSaveError = _main.WorkspaceSaveError is not null;
        SaveStateText = Text(HasSaveError ? "Str.Workspace.SaveError" : "Str.Status.Saved");
    }

    /// <summary>Runs <paramref name="action"/> on the calling thread's synchronization context
    /// (the UI thread in the app) after <paramref name="delay"/>. Without a context nothing is
    /// scheduled and the message simply stays.</summary>
    private static IDisposable ScheduleOnCurrentContext(TimeSpan delay, Action action)
    {
        if (System.Threading.SynchronizationContext.Current is not { } context) return new System.Threading.CancellationTokenSource();
        return new System.Threading.Timer(_ => context.Post(_ => action(), null), null, delay, System.Threading.Timeout.InfiniteTimeSpan);
    }

    private static string? ProxyHost(string? proxyUrl)
    {
        if (string.IsNullOrWhiteSpace(proxyUrl)) return null;
        return Uri.TryCreate(proxyUrl.Trim(), UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
            ? uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}"
            : proxyUrl.Trim();
    }

    private void ObserveTab(RequestTabViewModel? tab)
    {
        if (_observedTab is not null) _observedTab.PropertyChanged -= OnTabPropertyChanged;
        _observedTab = tab;
        if (tab is not null) tab.PropertyChanged += OnTabPropertyChanged;
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not RequestTabViewModel tab) return;
        switch (e.PropertyName)
        {
            case nameof(RequestTabViewModel.ResponseActionMessage) when tab.ResponseActionMessage is { } message:
                Post(message);
                break;
            // Only a save clears the dirty flag of an open tab.
            case nameof(RequestTabViewModel.IsDirty) when !tab.IsDirty && tab.CollectionProvider?.Invoke() is not null:
                Post(string.Format(CultureInfo.CurrentCulture, Text("Str.Status.RequestSaved"), tab.Name));
                Refresh();
                break;
            case nameof(RequestTabViewModel.ProxyUrl) or nameof(RequestTabViewModel.RequestId):
                Refresh();
                break;
        }
    }

    private static string Text(string key) => AppLocalization.Get(key);
}
