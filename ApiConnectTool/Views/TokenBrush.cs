using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;

namespace ApiConnectTool.Views;

/// <summary>Attached properties that bind a control's foreground or background to a theme token
/// chosen at run time (for example from a method or status class), and keep following the token
/// when the theme changes. Use it where the key comes from a binding; use
/// <c>{DynamicResource}</c> where the key is fixed.</summary>
public static class TokenBrush
{
    public static readonly AttachedProperty<string?> ForegroundProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Foreground", typeof(TokenBrush));

    public static readonly AttachedProperty<string?> BackgroundProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Background", typeof(TokenBrush));

    public static readonly AttachedProperty<string?> BorderBrushProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("BorderBrush", typeof(TokenBrush));

    private static readonly AttachedProperty<IDisposable?> BorderBrushSubscriptionProperty =
        AvaloniaProperty.RegisterAttached<Control, IDisposable?>("BorderBrushSubscription", typeof(TokenBrush));

    private static readonly AttachedProperty<IDisposable?> ForegroundSubscriptionProperty =
        AvaloniaProperty.RegisterAttached<Control, IDisposable?>("ForegroundSubscription", typeof(TokenBrush));

    private static readonly AttachedProperty<IDisposable?> BackgroundSubscriptionProperty =
        AvaloniaProperty.RegisterAttached<Control, IDisposable?>("BackgroundSubscription", typeof(TokenBrush));

    static TokenBrush()
    {
        ForegroundProperty.Changed.AddClassHandler<Control>((control, change) =>
            Rebind(control, TextElement.ForegroundProperty, ForegroundSubscriptionProperty, change.NewValue as string));
        BackgroundProperty.Changed.AddClassHandler<Control>((control, change) =>
            Rebind(control, BackgroundTarget(control), BackgroundSubscriptionProperty, change.NewValue as string));
        BorderBrushProperty.Changed.AddClassHandler<Control>((control, change) =>
            Rebind(control, control is Border ? Border.BorderBrushProperty : control is TemplatedControl ? TemplatedControl.BorderBrushProperty : null,
                BorderBrushSubscriptionProperty, change.NewValue as string));
    }

    public static string? GetForeground(Control control) => control.GetValue(ForegroundProperty);
    public static void SetForeground(Control control, string? value) => control.SetValue(ForegroundProperty, value);
    public static string? GetBackground(Control control) => control.GetValue(BackgroundProperty);
    public static void SetBackground(Control control, string? value) => control.SetValue(BackgroundProperty, value);

    public static string? GetBorderBrush(Control control) => control.GetValue(BorderBrushProperty);
    public static void SetBorderBrush(Control control, string? value) => control.SetValue(BorderBrushProperty, value);

    private static AvaloniaProperty? BackgroundTarget(Control control) => control switch
    {
        Border => Border.BackgroundProperty,
        Panel => Panel.BackgroundProperty,
        TemplatedControl => TemplatedControl.BackgroundProperty,
        TextBlock => TextBlock.BackgroundProperty,
        _ => null
    };

    private static void Rebind(Control control, AvaloniaProperty? target,
        AttachedProperty<IDisposable?> subscription, string? key)
    {
        control.GetValue(subscription)?.Dispose();
        control.SetValue(subscription, null);
        if (target is null || string.IsNullOrEmpty(key)) return;
        control.SetValue(subscription, control.Bind(target, control.GetResourceObservable(key)));
    }
}
