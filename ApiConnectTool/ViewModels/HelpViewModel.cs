using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApiConnectTool.Help;

namespace ApiConnectTool.ViewModels;

public partial class HelpViewModel : ViewModelBase
{
    private readonly Stack<HelpTopic> _backStack = new();
    private readonly Stack<HelpTopic> _forwardStack = new();
    private readonly Dictionary<string, HelpTopic> _topicsById;
    private bool _suppressHistoryPush;

    [ObservableProperty]
    private HelpTopic? _selectedTopic;

    public HelpViewModel()
        : this(HelpTopicCatalog.Load())
    {
    }

    public HelpViewModel(IReadOnlyList<HelpTopic> topics, string? initialTopicId = null)
    {
        ArgumentNullException.ThrowIfNull(topics);
        Topics = topics;
        _topicsById = topics.ToDictionary(topic => topic.Id, StringComparer.OrdinalIgnoreCase);

        _suppressHistoryPush = true;
        SelectedTopic = initialTopicId is not null && _topicsById.TryGetValue(initialTopicId, out var initial)
            ? initial
            : topics.FirstOrDefault();
        _suppressHistoryPush = false;
    }

    public IReadOnlyList<HelpTopic> Topics { get; }

    public bool CanGoBack => _backStack.Count > 0;

    public bool CanGoForward => _forwardStack.Count > 0;

    public bool CanResolveTopic(string topicId) =>
        !string.IsNullOrWhiteSpace(topicId) && _topicsById.ContainsKey(topicId);

    public bool NavigateTo(string topicId)
    {
        if (string.IsNullOrWhiteSpace(topicId) || !_topicsById.TryGetValue(topicId, out var target))
        {
            return false;
        }

        SelectedTopic = target;
        return true;
    }

    partial void OnSelectedTopicChanging(HelpTopic? oldValue, HelpTopic? newValue)
    {
        if (_suppressHistoryPush || oldValue is null || newValue is null || ReferenceEquals(oldValue, newValue))
        {
            return;
        }

        _backStack.Push(oldValue);
        _forwardStack.Clear();
    }

    partial void OnSelectedTopicChanged(HelpTopic? value)
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        BackCommand.NotifyCanExecuteChanged();
        ForwardCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => NavigateHistory(_backStack, _forwardStack);

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void Forward() => NavigateHistory(_forwardStack, _backStack);

    private void NavigateHistory(Stack<HelpTopic> source, Stack<HelpTopic> destination)
    {
        if (source.Count == 0) return;

        if (SelectedTopic is { } current)
        {
            destination.Push(current);
        }

        _suppressHistoryPush = true;
        SelectedTopic = source.Pop();
        _suppressHistoryPush = false;
    }
}
