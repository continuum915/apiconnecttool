using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.ViewModels;

/// <summary>The toolbar picker for the active tab's collection environment. It lists "No
/// environment" and the collection's environments, and selecting one sets that collection's
/// active environment only. Draft tabs have no collection, so the picker is disabled for them.</summary>
public sealed partial class CollectionEnvironmentPickerViewModel : ObservableObject
{
    private readonly Func<RequestCollection?> _activeCollection;
    private readonly Func<IEnumerable<RequestCollection>> _allCollections;
    private readonly Action _persist;
    private bool _isRefreshing;
    private bool _restoringSelection;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(UseInEveryCollectionText), nameof(SelectedItem))]
    [NotifyCanExecuteChangedFor(nameof(UseInEveryCollectionCommand))]
    private EnvironmentChoice? _selectedChoice;

    /// <summary>The result of the last "Use in every collection", naming the skipped collections.</summary>
    [ObservableProperty] private string? _useInEveryCollectionReport;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsEnabled), nameof(ToolTip), nameof(Caption))]
    [NotifyCanExecuteChangedFor(nameof(ManageEnvironmentsCommand))]
    private RequestCollection? _collection;

    public CollectionEnvironmentPickerViewModel(Func<RequestCollection?> activeCollection, Func<IEnumerable<RequestCollection>> allCollections, Action persist)
    {
        _activeCollection = activeCollection;
        _allCollections = allCollections;
        _persist = persist;
        UseInEveryCollectionAction = new EnvironmentPickerAction(UseInEveryCollectionText, UseInEveryCollectionCommand);
        ManageAction = new EnvironmentPickerAction(string.Empty, ManageEnvironmentsCommand);
        Refresh();
    }

    public ObservableCollection<EnvironmentChoice> Choices { get; } = new();

    /// <summary>What the drop-down lists: the choices, then "Use '&lt;name&gt;' in every collection"
    /// and "Manage &lt;collection&gt; environments..." while a collection is active.</summary>
    public ObservableCollection<object> Items { get; } = new();

    public EnvironmentPickerAction UseInEveryCollectionAction { get; }
    public EnvironmentPickerAction ManageAction { get; }

    /// <summary>Set by the main view model to open the environment editor for a collection.</summary>
    public Action<RequestCollection>? ManageRequested { get; set; }

    /// <summary>The drop-down's selected item. Choosing an action runs it and keeps the current
    /// environment selected.</summary>
    public object? SelectedItem
    {
        get => _restoringSelection ? null : SelectedChoice;
        set
        {
            if (value is EnvironmentChoice choice) SelectedChoice = choice;
            else if (value is EnvironmentPickerAction action)
            {
                action.Run();
                // The combo box is still applying the click; restore the environment afterwards.
                // Bindings skip a value equal to the last one they saw, so pass through null first.
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _restoringSelection = true;
                    OnPropertyChanged(nameof(SelectedItem));
                    _restoringSelection = false;
                    OnPropertyChanged(nameof(SelectedItem));
                });
            }
        }
    }

    /// <summary>"Environment for &lt;collection&gt;", shown above the picker.</summary>
    public string Caption => Collection is null
        ? AppLocalization.Get("Str.Toolbar.EnvironmentForDraft")
        : string.Format(CultureInfo.CurrentCulture, AppLocalization.Get("Str.Toolbar.EnvironmentFor"), Collection.Name);

    private bool HasCollection() => Collection is not null;

    [RelayCommand(CanExecute = nameof(HasCollection))]
    private void ManageEnvironments()
    {
        if (Collection is { } collection) ManageRequested?.Invoke(collection);
    }

    public bool IsEnabled => Collection is not null;

    public string ToolTip => Collection is null
        ? AppLocalization.Get("Str.CollectionEnvironment.DraftTab")
        : string.Format(System.Globalization.CultureInfo.CurrentCulture,
            AppLocalization.Get("Str.CollectionEnvironment.PickerTip"), Collection.Name);

    /// <summary>Rereads the active tab's collection, its environments, and its active one. The
    /// choice list is rebuilt only when it changed, so an open dropdown is not disturbed.</summary>
    public void Refresh()
    {
        _isRefreshing = true;
        try
        {
            Collection = _activeCollection();
            var desired = Collection is null ? [] : EnvironmentChoice.ForCollection(Collection);
            if (!Choices.SequenceEqual(desired))
            {
                Choices.Clear();
                foreach (var choice in desired) Choices.Add(choice);
            }

            ManageAction.Text = Collection is null ? string.Empty
                : string.Format(CultureInfo.CurrentCulture, AppLocalization.Get("Str.CollectionEnvironment.ManageFor"), Collection.Name);
            object[] items = Collection is null ? [] : [.. Choices, UseInEveryCollectionAction, ManageAction];
            if (!Items.SequenceEqual(items))
            {
                Items.Clear();
                foreach (var item in items) Items.Add(item);
            }

            SelectedChoice = Collection is null ? null
                : Choices.FirstOrDefault(choice => choice.Environment is not null && choice.Environment == Collection.ActiveEnvironment)
                    ?? Choices.FirstOrDefault();
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    public string UseInEveryCollectionText => SelectedChoice?.Environment is { } environment
        ? string.Format(CultureInfo.CurrentCulture, AppLocalization.Get("Str.CollectionEnvironment.UseEverywhere"), environment.Name)
        : AppLocalization.Get("Str.CollectionEnvironment.UseEverywhereNone");

    private bool CanUseInEveryCollection() => SelectedChoice?.Environment is not null;

    /// <summary>Makes the environment named like the selected one (ignoring case) active in every
    /// collection that has one. Collections without such an environment keep their selection and
    /// are listed in <see cref="UseInEveryCollectionReport"/>.</summary>
    [RelayCommand(CanExecute = nameof(CanUseInEveryCollection))]
    private void UseInEveryCollection()
    {
        if (SelectedChoice?.Environment is not { } selected) return;
        var name = selected.Name.Trim();
        var matched = 0;
        var skipped = new List<string>();
        foreach (var collection in _allCollections())
        {
            var match = collection.Environments.FirstOrDefault(environment =>
                string.Equals(environment.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                skipped.Add(collection.Name);
                continue;
            }

            collection.ActiveEnvironmentId = match.Id;
            matched++;
        }

        UseInEveryCollectionReport = skipped.Count == 0
            ? string.Format(CultureInfo.CurrentCulture, AppLocalization.Get("Str.CollectionEnvironment.UsedEverywhere"), name, matched)
            : string.Format(CultureInfo.CurrentCulture, AppLocalization.Get("Str.CollectionEnvironment.UsedEverywhereSkipped"), name, matched,
                string.Join(", ", skipped.Select(collection => $"'{collection}'")));
        _persist();
    }

    [RelayCommand]
    private void DismissUseInEveryCollectionReport() => UseInEveryCollectionReport = null;

    partial void OnSelectedChoiceChanged(EnvironmentChoice? value)
    {
        UseInEveryCollectionAction.Text = UseInEveryCollectionText;
        if (_isRefreshing || value is null || Collection is null || Collection.ActiveEnvironmentId == value.Environment?.Id) return;
        Collection.ActiveEnvironmentId = value.Environment?.Id;
        _persist();
    }
}
