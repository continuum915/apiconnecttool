using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ApiConnectTool.Converters
{
    public class BoolToIconConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                if (parameter?.ToString() == "Layout")
                {
                    return b ? "⊟" : "◫"; // Vertical stacked vs Side-by-side
                }

                // Every other caller is a secret-visibility toggle (password/token fields).
                return b ? "Hide" : "Show";
            }
            return "Show";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
