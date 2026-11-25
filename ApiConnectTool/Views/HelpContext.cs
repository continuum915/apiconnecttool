using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ApiConnectTool.Views;

public static class HelpContext
{
    public const string DefaultTopicId = "contents";

    public static readonly AttachedProperty<string?> TopicIdProperty =
        AvaloniaProperty.RegisterAttached<AvaloniaObject, string?>("TopicId", typeof(HelpContext));

    static HelpContext()
    {
        InputElement.KeyDownEvent.AddClassHandler<Window>(OnWindowPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    public static void EnsureInitialized()
    {
        // Triggers static constructor to register the global Window F1 tunnel handler.
    }

    public static string? GetTopicId(AvaloniaObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(TopicIdProperty);
    }

    public static void SetTopicId(AvaloniaObject element, string? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TopicIdProperty, value);
    }

    public static string ResolveTopicId(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var focused = window.FocusManager?.GetFocusedElement() as AvaloniaObject;
        return ResolveTopicId(focused ?? window, window);
    }

    public static string ResolveTopicId(AvaloniaObject? startElement, Window? rootWindow = null)
    {
        var visited = new HashSet<AvaloniaObject>();
        var current = startElement;

        while (current is not null && visited.Add(current))
        {
            if (TryGetElementOrTabTopicId(current, out var directId))
            {
                return directId;
            }

            current = (current as StyledElement)?.Parent as AvaloniaObject
                      ?? (current as Visual)?.GetVisualParent();
        }

        for (var visual = startElement as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visited.Add(visual) && TryGetElementOrTabTopicId(visual, out var visualId))
            {
                return visualId;
            }
        }

        if (rootWindow is not null && TryGetNonEmptyTopicId(rootWindow, out var windowId))
        {
            return windowId;
        }

        return DefaultTopicId;
    }

    public static HelpWindow OpenForWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var topicId = ResolveTopicId(window);
        return HelpWindow.Show(window, topicId);
    }

    private static void OnWindowPreviewKeyDown(Window window, KeyEventArgs e)
    {
        if (e.Handled || window is HelpWindow) return;
        if (!KeyboardShortcuts.Get(ShortcutId.OpenHelp).Gesture.Matches(e)) return;

        OpenForWindow(window);
        e.Handled = true;
    }

    private static bool TryGetElementOrTabTopicId(AvaloniaObject element, out string topicId)
    {
        if (TryGetNonEmptyTopicId(element, out topicId))
        {
            return true;
        }

        if (element is TabControl tabControl && TryGetSelectedTabTopicId(tabControl, out topicId))
        {
            return true;
        }

        topicId = string.Empty;
        return false;
    }

    private static bool TryGetSelectedTabTopicId(TabControl tabControl, out string topicId)
    {
        if (tabControl.SelectedItem is AvaloniaObject selectedItem &&
            TryGetNonEmptyTopicId(selectedItem, out topicId))
        {
            return true;
        }

        if (tabControl.SelectedIndex >= 0 &&
            tabControl.ContainerFromIndex(tabControl.SelectedIndex) is { } container &&
            TryGetNonEmptyTopicId(container, out topicId))
        {
            return true;
        }

        topicId = string.Empty;
        return false;
    }

    private static bool TryGetNonEmptyTopicId(AvaloniaObject element, out string topicId)
    {
        var value = GetTopicId(element);
        if (!string.IsNullOrWhiteSpace(value))
        {
            topicId = value.Trim();
            return true;
        }

        topicId = string.Empty;
        return false;
    }
}
