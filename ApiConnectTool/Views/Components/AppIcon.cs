using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ApiConnectTool.Views.Components;

/// <summary>Draws one <c>ApiConnectTool.Icon.*</c> geometry with theme token brushes. Call sites name the
/// icon and, when needed, a token key; they never pass colors. Collection and folder icons use the
/// <c>ApiConnectTool.Brush.Icon.*</c> fill and stroke tokens unless a key is given explicitly.</summary>
public sealed class AppIcon : Control
{
    /// <summary>Icon units per side; geometries are drawn on a 16 by 16 grid.</summary>
    public const double GridSize = 16;
    private const double StrokeThickness = 1.25;
    private const double DisabledOpacity = 0.45;

    public static readonly StyledProperty<string?> KindProperty =
        AvaloniaProperty.Register<AppIcon, string?>(nameof(Kind));

    /// <summary>Token key for the outline; defaults to <c>ApiConnectTool.Brush.Text</c>.</summary>
    public static readonly StyledProperty<string?> BrushKeyProperty =
        AvaloniaProperty.Register<AppIcon, string?>(nameof(BrushKey));

    /// <summary>Token key for the fill; only collection and folder icons have one by default.</summary>
    public static readonly StyledProperty<string?> FillKeyProperty =
        AvaloniaProperty.Register<AppIcon, string?>(nameof(FillKey));

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<AppIcon, double>(nameof(Size), 16);

    static AppIcon()
    {
        AffectsRender<AppIcon>(KindProperty, BrushKeyProperty, FillKeyProperty, IsEnabledProperty);
        AffectsMeasure<AppIcon>(SizeProperty);
    }

    public AppIcon()
    {
        ResourcesChanged += (_, _) => InvalidateVisual();
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public string? Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public string? BrushKey { get => GetValue(BrushKeyProperty); set => SetValue(BrushKeyProperty, value); }
    public string? FillKey { get => GetValue(FillKeyProperty); set => SetValue(FillKeyProperty, value); }
    public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    public static string GeometryKey(string kind) => "ApiConnectTool.Icon." + kind;

    /// <summary>Effective outline token for an icon kind.</summary>
    public static string StrokeKeyFor(string? kind, string? brushKey) => brushKey ?? kind switch
    {
        "Folder" => "ApiConnectTool.Brush.Icon.FolderStroke",
        "Collection" => "ApiConnectTool.Brush.Icon.CollectionStroke",
        _ => "ApiConnectTool.Brush.Text"
    };

    /// <summary>Effective fill token for an icon kind, or null for outline-only icons.</summary>
    public static string? FillKeyFor(string? kind, string? fillKey) => fillKey ?? kind switch
    {
        "Folder" => "ApiConnectTool.Brush.Icon.FolderFill",
        "Collection" => "ApiConnectTool.Brush.Icon.CollectionFill",
        _ => null
    };

    public Geometry? ResolveGeometry() =>
        Kind is { Length: > 0 } kind && this.TryFindResource(GeometryKey(kind), ActualThemeVariant, out var value)
            ? value as Geometry
            : null;

    private IBrush? ResolveBrush(string? key) =>
        key is not null && this.TryFindResource(key, ActualThemeVariant, out var value) ? value as IBrush : null;

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext context)
    {
        var geometry = ResolveGeometry();
        if (geometry is null) return;
        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (side <= 0) return;

        var stroke = ResolveBrush(StrokeKeyFor(Kind, BrushKey));
        var fill = ResolveBrush(FillKeyFor(Kind, FillKey));
        var scale = side / GridSize;
        var offset = new Point((Bounds.Width - side) / 2, (Bounds.Height - side) / 2);
        using var opacity = IsEffectivelyEnabled ? (IDisposable?)null : context.PushOpacity(DisabledOpacity);
        using var transform = context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset.X, offset.Y));
        var pen = stroke is null ? null : new Pen(stroke, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        context.DrawGeometry(fill, pen, geometry);
    }
}
