using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ApiConnectTool.Converters
{
    /// <summary>Masks a row's secret value unless it's marked IsSecret=false or visibility has
    /// been toggled on. Bind as a MultiBinding with [row.IsSecret, editor.IsSecretVisible].</summary>
    public class SecretPasswordCharConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            var isSecret = values.Count > 0 && values[0] is true;
            var isVisible = values.Count > 1 && values[1] is true;
            return isSecret && !isVisible ? '●' : '\0';
        }
    }
}
