using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ApiConnectTool.Help;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Views;

public enum ShortcutId
{
    SendRequest,
    CancelSend,
    NewTab,
    CloseTab,
    NextTab,
    PreviousTab,
    FocusUrl,
    OpenEnvironments,
    ChooseCollectionEnvironment,
    SaveRequest,
    SaveRequestAs,
    OpenHelp
}

public sealed record KeyboardShortcut(
    ShortcutId Id,
    KeyGesture Gesture,
    ICommand Command,
    string DescriptionKey,
    string? MenuItemName = null)
{
    public string Description => AppLocalization.Get(DescriptionKey);
}

public static class KeyboardShortcuts
{
    public const string HelpTopicId = "keyboard-shortcuts";

    public static IReadOnlyList<KeyboardShortcut> Table { get; } =
    [
        new(
            ShortcutId.SendRequest,
            new KeyGesture(Key.Enter, KeyModifiers.Control),
            CreateCommand(
                window =>
                {
                    if (window.ViewModel?.ActiveTab is { } tab && tab.SendCommand.CanExecute(null))
                    {
                        tab.SendCommand.Execute(null);
                    }
                },
                window => window.ViewModel?.ActiveTab is not null),
            "Str.Shortcuts.Send",
            MenuItemName: "SendMenuItem"),
        new(
            ShortcutId.CancelSend,
            new KeyGesture(Key.Escape),
            CreateCommand(
                window => window.ViewModel?.ActiveTab?.CancelCommand.Execute(null),
                window => window.ViewModel?.ActiveTab is { IsSending: true } tab && tab.CancelCommand.CanExecute(null)),
            "Str.Shortcuts.CancelSend",
            MenuItemName: "CancelMenuItem"),
        new(
            ShortcutId.NewTab,
            new KeyGesture(Key.T, KeyModifiers.Control),
            CreateCommand(
                window => window.ViewModel?.AddTabCommand.Execute(null),
                window => window.ViewModel is not null),
            "Str.Shortcuts.NewTab",
            MenuItemName: "NewRequestMenuItem"),
        new(
            ShortcutId.CloseTab,
            new KeyGesture(Key.W, KeyModifiers.Control),
            CreateCommand(
                window =>
                {
                    if (window.ViewModel is { ActiveTab: { } tab } vm)
                    {
                        vm.CloseTabCommand.Execute(tab);
                    }
                },
                window => window.ViewModel?.ActiveTab is not null),
            "Str.Shortcuts.CloseTab",
            MenuItemName: "CloseTabMenuItem"),
        new(
            ShortcutId.NextTab,
            new KeyGesture(Key.Tab, KeyModifiers.Control),
            CreateCommand(
                window => window.ViewModel?.SwitchTab(1),
                window => window.ViewModel is { OpenRequests.Count: > 0 }),
            "Str.Shortcuts.NextTab"),
        new(
            ShortcutId.PreviousTab,
            new KeyGesture(Key.Tab, KeyModifiers.Control | KeyModifiers.Shift),
            CreateCommand(
                window => window.ViewModel?.SwitchTab(-1),
                window => window.ViewModel is { OpenRequests.Count: > 0 }),
            "Str.Shortcuts.PreviousTab"),
        new(
            ShortcutId.FocusUrl,
            new KeyGesture(Key.L, KeyModifiers.Control),
            CreateCommand(
                window => window.FocusUrlBox(),
                window => window.ViewModel is not null),
            "Str.Shortcuts.FocusUrl"),
        new(
            ShortcutId.OpenEnvironments,
            new KeyGesture(Key.E, KeyModifiers.Control),
            CreateCommand(
                window => window.OpenEnvironmentsForContext(),
                window => window.ViewModel is not null),
            "Str.Shortcuts.OpenEnvironments",
            MenuItemName: "EnvironmentsMenuItem"),
        new(
            ShortcutId.ChooseCollectionEnvironment,
            new KeyGesture(Key.E, KeyModifiers.Control | KeyModifiers.Shift),
            CreateCommand(
                window => window.OpenCollectionEnvironmentPicker(),
                window => window.ViewModel?.CollectionEnvironmentPicker.IsEnabled == true),
            "Str.Shortcuts.ChooseCollectionEnvironment"),
        new(
            ShortcutId.SaveRequest,
            new KeyGesture(Key.S, KeyModifiers.Control),
            CreateCommand(
                window => window.ViewModel?.SaveCommand.Execute(null),
                window => window.ViewModel is not null),
            "Str.Shortcuts.Save",
            MenuItemName: "SaveRequestMenuItem"),
        new(
            ShortcutId.SaveRequestAs,
            new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Shift),
            CreateCommand(
                window => window.ViewModel?.SaveAsCommand.Execute(null),
                window => window.ViewModel is not null),
            "Str.Shortcuts.SaveAs",
            MenuItemName: "SaveRequestAsMenuItem"),
        new(
            ShortcutId.OpenHelp,
            new KeyGesture(Key.F1),
            CreateCommand(window => HelpContext.OpenForWindow(window)),
            "Str.Shortcuts.OpenHelp",
            MenuItemName: "ApiConnectToolHelpMenuItem")
    ];

    public static KeyboardShortcut Get(ShortcutId id) => Table.First(shortcut => shortcut.Id == id);

    public static string FormatGesture(KeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        return gesture.ToString();
    }

    public static HelpTopic BuildHelpTopic(int order = 100)
    {
        var title = AppLocalization.Get("Str.Help.ShortcutsTopicTitle");
        var intro = AppLocalization.Get("Str.Help.ShortcutsTopicIntro");
        var shortcutHeader = AppLocalization.Get("Str.Help.ShortcutColumn");
        var actionHeader = AppLocalization.Get("Str.Help.ActionColumn");

        var builder = new StringBuilder();
        builder.Append("# ").AppendLine(title).AppendLine();
        builder.AppendLine(intro).AppendLine();
        builder.Append("| ").Append(shortcutHeader).Append(" | ").Append(actionHeader).AppendLine(" |");
        builder.AppendLine("|---|---|");

        foreach (var entry in Table)
        {
            builder.Append("| `")
                .Append(FormatGesture(entry.Gesture))
                .Append("` | ")
                .Append(entry.Description)
                .AppendLine(" |");
        }

        return new HelpTopic(
            HelpTopicId,
            title,
            order,
            builder.ToString().Trim(),
            "<generated:KeyboardShortcuts.Table>");
    }

    public static void Register(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        HelpContext.EnsureInitialized();
        window.KeyBindings.Clear();
        foreach (var entry in Table)
        {
            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = entry.Gesture,
                Command = entry.Command,
                CommandParameter = window
            });
            if (entry.MenuItemName is not null && window.FindControl<MenuItem>(entry.MenuItemName) is { } menuItem)
            {
                menuItem.InputGesture = entry.Gesture;
            }
        }

        window.AddHandler(InputElement.KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        void OnPreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Handled) return;
            foreach (var entry in Table)
            {
                if (!entry.Gesture.Matches(e)) continue;
                if (!entry.Command.CanExecute(window)) return;

                if (entry.Id != ShortcutId.CancelSend)
                {
                    CommitFocusedDataGridEdit(window);
                }

                entry.Command.Execute(window);
                e.Handled = true;
                return;
            }
        }
    }

    private static void CommitFocusedDataGridEdit(MainWindow window)
    {
        if (window.FocusManager?.GetFocusedElement() is Visual focused &&
            focused.FindAncestorOfType<DataGrid>(includeSelf: true) is { } grid)
        {
            grid.CommitEdit();
        }
    }

    private static ICommand CreateCommand(Action<MainWindow> execute, Func<MainWindow, bool>? canExecute = null) =>
        new WindowShortcutCommand(execute, canExecute);

    private sealed class WindowShortcutCommand(
        Action<MainWindow> execute,
        Func<MainWindow, bool>? canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) =>
            parameter is MainWindow window && (canExecute?.Invoke(window) ?? true);

        public void Execute(object? parameter)
        {
            if (parameter is MainWindow window && CanExecute(window))
            {
                execute(window);
            }
        }
    }
}
