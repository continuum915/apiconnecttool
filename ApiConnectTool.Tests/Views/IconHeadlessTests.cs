using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SkiaSharp;
using ApiConnectTool.Tests.Guard;
using ApiConnectTool.Views.Components;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public partial class IconHeadlessTests
{
    public static readonly string[] IconNames =
    [
        "NewRequest", "NewCollection", "Import", "Export", "Run", "Stop", "History", "Console", "Variables",
        "Environment", "Save", "Copy", "Delete", "Duplicate", "Add", "Search", "Collection", "Folder",
        "ChevronRight", "ChevronDown", "Close", "Check", "Cross", "Warning", "Error", "Info", "Network",
        "Script", "Compare", "Resend", "Lock"
    ];

    [GeneratedRegex("<StreamGeometry x:Key=\"ApiConnectTool\\.Icon\\.(?<name>\\w+)\">(?<data>[^<]+)</StreamGeometry>")]
    private static partial Regex GeometryEntry();

    [Fact]
    public Task EveryIconResolvesToAGeometry() => HeadlessSession.RunAsync(() =>
    {
        var window = new Window { Content = new Border() };
        window.Show();
        try
        {
            foreach (var name in IconNames)
            {
                Assert.True(window.TryFindResource(AppIcon.GeometryKey(name), out var resource), name);
                Assert.IsAssignableFrom<Geometry>(resource);
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task IconControlResolvesGeometryAndSizesItself() => HeadlessSession.RunAsync(() =>
    {
        var icon = new AppIcon { Kind = "Folder", Size = 22, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Content = icon };
        window.Show();
        try
        {
            window.UpdateLayout();
            Assert.NotNull(icon.ResolveGeometry());
            Assert.Equal(22, icon.Bounds.Width);
            Assert.Equal(22, icon.Bounds.Height);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("Folder", null, null, "ApiConnectTool.Brush.Icon.FolderStroke", "ApiConnectTool.Brush.Icon.FolderFill")]
    [InlineData("Collection", null, null, "ApiConnectTool.Brush.Icon.CollectionStroke", "ApiConnectTool.Brush.Icon.CollectionFill")]
    [InlineData("Run", null, null, "ApiConnectTool.Brush.Text", null)]
    [InlineData("Warning", "ApiConnectTool.Brush.Warning", null, "ApiConnectTool.Brush.Warning", null)]
    public void IconsUseTokenBrushes(string kind, string? brushKey, string? fillKey, string stroke, string? fill)
    {
        Assert.Equal(stroke, AppIcon.StrokeKeyFor(kind, brushKey));
        Assert.Equal(fill, AppIcon.FillKeyFor(kind, fillKey));
    }

    [Fact]
    public void IconFileDefinesExactlyTheIconSet()
    {
        var names = ReadIconData().Keys.Order().ToArray();
        Assert.Equal(IconNames.Order().ToArray(), names);
    }

    /// <summary>Rasterizes each icon's path data the way <see cref="AppIcon"/> draws it (1.25-unit
    /// round-capped stroke on a 16-unit grid) at 15 px and 22 px for 100% to 200% scaling, and checks
    /// that nothing is clipped and that most inked pixels are solid rather than smeared.</summary>
    [Theory]
    [InlineData(15, 1.0)] [InlineData(15, 1.25)] [InlineData(15, 1.5)] [InlineData(15, 2.0)]
    [InlineData(22, 1.0)] [InlineData(22, 1.25)] [InlineData(22, 1.5)] [InlineData(22, 2.0)]
    public void IconsRenderCrisplyAtEachSizeAndScale(int size, double scaling)
    {
        foreach (var (name, data) in ReadIconData())
        {
            using var path = SKPath.ParseSvgPathData(data);
            Assert.True(path is not null, $"{name}: path data does not parse");
            var bounds = path!.TightBounds;
            const float halfStroke = 0.625f;
            Assert.True(bounds.Left - halfStroke >= 0 && bounds.Top - halfStroke >= 0 &&
                        bounds.Right + halfStroke <= 16 && bounds.Bottom + halfStroke <= 16,
                $"{name}: geometry {bounds} plus stroke leaves the 16-unit grid");

            var pixels = (int)Math.Ceiling(size * scaling);
            using var bitmap = new SKBitmap(pixels, pixels, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bitmap))
            using (var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.25f,
                       StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, Color = SKColors.Black })
            {
                canvas.Clear(SKColors.Transparent);
                canvas.Scale((float)(size * scaling / 16.0));
                canvas.DrawPath(path, paint);
            }

            var alphas = bitmap.Pixels.Select(pixel => pixel.Alpha).Where(alpha => alpha > 0).ToArray();
            Assert.True(alphas.Length > 0, $"{name} at {size}px x{scaling}: nothing drawn");
            var solid = alphas.Count(alpha => alpha >= 128) / (double)alphas.Length;
            // A pure diagonal (Close, Cross) at 15 px is about 37% solid after anti-aliasing; blurry
            // hairlines or sub-pixel detail fall well below a third.
            Assert.True(solid >= 1 / 3.0, $"{name} at {size}px x{scaling}: only {solid:P0} of inked pixels are solid");
        }
    }

    private static Dictionary<string, string> ReadIconData()
    {
        var file = Path.Combine(HardcodingScanner.FindRepositoryRoot(), "ApiConnectTool", "Styles", "Icons.axaml");
        return GeometryEntry().Matches(File.ReadAllText(file))
            .ToDictionary(match => match.Groups["name"].Value, match => match.Groups["data"].Value.Trim());
    }
}
