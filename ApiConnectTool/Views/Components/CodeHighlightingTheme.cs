using System;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.Highlighting;
using ApiConnectTool.Converters;

namespace ApiConnectTool.Views.Components;

/// <summary>Recolors AvaloniaEdit's built-in highlighting definitions from the
/// <c>ApiConnectTool.Brush.Syntax.*</c> tokens of the active theme, so the editor needs no second palette.</summary>
public static class CodeHighlightingTheme
{
    public static void Apply(IHighlightingDefinition? definition, IResourceHost scope)
    {
        if (definition is null) return;
        foreach (var color in definition.NamedHighlightingColors)
        {
            if (color.IsFrozen) continue;
            if (ThemeBrush.Find(TokenFor(color.Name), scope) is ISolidColorBrush brush)
                color.Foreground = new SimpleHighlightingBrush(brush.Color);
            color.Background = null;
        }
    }

    /// <summary>Maps a highlighting color name (Json, XML, HTML, CSS, JavaScript) to a token key.</summary>
    public static string TokenFor(string? name)
    {
        var value = name ?? string.Empty;
        bool Has(string part) => value.Contains(part, StringComparison.OrdinalIgnoreCase);
        if (Has("Comment")) return "ApiConnectTool.Brush.Syntax.Comment";
        if (Has("Number") || Has("Digit")) return "ApiConnectTool.Brush.Syntax.Number";
        if (Has("Bool") || Has("Null") || Has("Literal") || Has("Keyword")) return "ApiConnectTool.Brush.Syntax.Literal";
        if (Has("AttributeValue") || Has("String") || Has("Char") || Has("Regex") || Has("CData") ||
            value.Equals("Value", StringComparison.OrdinalIgnoreCase)) return "ApiConnectTool.Brush.Syntax.String";
        if (Has("Field") || Has("Property") || Has("Attribute") || Has("Tag") || Has("Class") || Has("Selector"))
            return "ApiConnectTool.Brush.Syntax.Key";
        return "ApiConnectTool.Brush.Text";
    }
}
