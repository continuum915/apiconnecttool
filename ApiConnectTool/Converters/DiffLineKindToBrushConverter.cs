using System;
using System.Globalization;
using Avalonia.Data.Converters;
using ApiConnectTool.Services;

namespace ApiConnectTool.Converters;

/// <summary>Maps diff line kinds to the success and danger background tokens of the active theme.</summary>
public sealed class DiffLineKindToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DiffLineKind.Added => ThemeBrush.Find("ApiConnectTool.Brush.SuccessBackground"),
        DiffLineKind.Removed => ThemeBrush.Find("ApiConnectTool.Brush.DangerBackground"),
        _ => null
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
