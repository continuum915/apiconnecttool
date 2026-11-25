using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ApiConnectTool.Models;
using ApiConnectTool.Services;
using ApiConnectTool.Views.Components;
using Xunit;

namespace ApiConnectTool.Tests.Views;

public class ThemeTokenHeadlessTests
{
    /// <summary>The token table from the classic desktop UI spec, ticket 01.</summary>
    public static readonly string[] TokenKeys =
    [
        "ApiConnectTool.Brush.WindowBackground", "ApiConnectTool.Brush.Surface", "ApiConnectTool.Brush.Chrome", "ApiConnectTool.Brush.StatusBar",
        "ApiConnectTool.Brush.Pane", "ApiConnectTool.Brush.Input", "ApiConnectTool.Brush.Border", "ApiConnectTool.Brush.BorderSubtle", "ApiConnectTool.Brush.Grip",
        "ApiConnectTool.Brush.Text", "ApiConnectTool.Brush.TextMuted", "ApiConnectTool.Brush.Hover", "ApiConnectTool.Brush.Selection",
        "ApiConnectTool.Brush.SelectionText", "ApiConnectTool.Brush.GridHeader", "ApiConnectTool.Brush.GridAlternateRow", "ApiConnectTool.Brush.InactiveTab",
        "ApiConnectTool.Brush.Accent", "ApiConnectTool.Brush.AccentBorder", "ApiConnectTool.Brush.OnAccent", "ApiConnectTool.Brush.AccentText",
        "ApiConnectTool.Brush.Success", "ApiConnectTool.Brush.SuccessBackground", "ApiConnectTool.Brush.SuccessBorder", "ApiConnectTool.Brush.SuccessFill",
        "ApiConnectTool.Brush.Danger", "ApiConnectTool.Brush.DangerBackground", "ApiConnectTool.Brush.DangerBorder", "ApiConnectTool.Brush.DangerText",
        "ApiConnectTool.Brush.DangerFill", "ApiConnectTool.Brush.OnDanger", "ApiConnectTool.Brush.Warning", "ApiConnectTool.Brush.WarningText",
        "ApiConnectTool.Brush.WarningBackground", "ApiConnectTool.Brush.OnWarning", "ApiConnectTool.Brush.Info", "ApiConnectTool.Brush.OverrideBackground",
        "ApiConnectTool.Brush.Variable", "ApiConnectTool.Brush.Script", "ApiConnectTool.Brush.Code.Background", "ApiConnectTool.Brush.Code.Gutter",
        "ApiConnectTool.Brush.Code.GutterText", "ApiConnectTool.Brush.Syntax.Key", "ApiConnectTool.Brush.Syntax.String", "ApiConnectTool.Brush.Syntax.Number",
        "ApiConnectTool.Brush.Syntax.Literal", "ApiConnectTool.Brush.Syntax.Comment", "ApiConnectTool.Brush.Method.Get", "ApiConnectTool.Brush.Method.Post",
        "ApiConnectTool.Brush.Method.Put", "ApiConnectTool.Brush.Method.Patch", "ApiConnectTool.Brush.Method.Delete", "ApiConnectTool.Brush.Method.Other",
        "ApiConnectTool.Brush.Icon.FolderFill", "ApiConnectTool.Brush.Icon.FolderStroke", "ApiConnectTool.Brush.Icon.CollectionFill",
        "ApiConnectTool.Brush.Icon.CollectionStroke"
    ];

    [Fact]
    public Task EveryThemeResolvesEveryToken() => HeadlessSession.RunAsync(() =>
    {
        var app = Application.Current!;
        var originalVariant = app.RequestedThemeVariant;
        var manager = new AppThemeManager(app);
        var window = new Window { Content = new Border() };
        try
        {
            window.Show();
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light, AppTheme.Win7Light })
            {
                manager.Apply(theme);
                Dispatcher.UIThread.RunJobs();
                foreach (var key in TokenKeys)
                {
                    Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var resource),
                        $"{theme}: {key} does not resolve");
                    Assert.IsAssignableFrom<ISolidColorBrush>(resource);
                }
            }
        }
        finally
        {
            window.Close();
            manager.Apply(AppTheme.Light);
            app.RequestedThemeVariant = originalVariant;
        }
    });

    [Fact]
    public Task LightAndDarkUseTheClassicPalette() => HeadlessSession.RunAsync(() =>
    {
        var app = Application.Current!;
        var originalVariant = app.RequestedThemeVariant;
        var manager = new AppThemeManager(app);
        var window = new Window { Content = new Border() };
        try
        {
            window.Show();
            manager.Apply(AppTheme.Light);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Color.Parse("#FFE8EAED"), Brush(window, "ApiConnectTool.Brush.WindowBackground"));
            Assert.Equal(Color.Parse("#FF1F5FBF"), Brush(window, "ApiConnectTool.Brush.Accent"));
            manager.Apply(AppTheme.Dark);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Color.Parse("#FF2B2D31"), Brush(window, "ApiConnectTool.Brush.WindowBackground"));
            Assert.Equal(Color.Parse("#FF4C8FE6"), Brush(window, "ApiConnectTool.Brush.Accent"));
            Assert.True(window.TryFindResource("SystemAccentColor", window.ActualThemeVariant, out var accent));
            Assert.Equal(Color.Parse("#FF4C8FE6"), Assert.IsType<Color>(accent));
        }
        finally
        {
            window.Close();
            manager.Apply(AppTheme.Light);
            app.RequestedThemeVariant = originalVariant;
        }
    });

    [Theory]
    [InlineData("FieldName", "ApiConnectTool.Brush.Syntax.Key")]
    [InlineData("String", "ApiConnectTool.Brush.Syntax.String")]
    [InlineData("Number", "ApiConnectTool.Brush.Syntax.Number")]
    [InlineData("Bool", "ApiConnectTool.Brush.Syntax.Literal")]
    [InlineData("Null", "ApiConnectTool.Brush.Syntax.Literal")]
    [InlineData("Comment", "ApiConnectTool.Brush.Syntax.Comment")]
    [InlineData("AttributeValue", "ApiConnectTool.Brush.Syntax.String")]
    [InlineData("AttributeName", "ApiConnectTool.Brush.Syntax.Key")]
    [InlineData("Punctuation", "ApiConnectTool.Brush.Text")]
    public void HighlightingColorsMapToSyntaxTokens(string name, string token) =>
        Assert.Equal(token, CodeHighlightingTheme.TokenFor(name));

    private static Color Brush(Window window, string key)
    {
        Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var resource));
        return Assert.IsAssignableFrom<ISolidColorBrush>(resource).Color;
    }
}
