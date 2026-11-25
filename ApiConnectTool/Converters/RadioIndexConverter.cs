using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace ApiConnectTool.Converters;

/// <summary>Binds a radio button to an integer index: checked when the value equals the
/// converter parameter, and choosing the button writes the parameter back.</summary>
public sealed class RadioIndexConverter : IValueConverter
{
    public static readonly RadioIndexConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int index && int.TryParse(parameter?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var expected) && index == expected;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && int.TryParse(parameter?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            ? index
            : BindingOperations.DoNothing;
}
