using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace ApiConnectTool.Converters
{
    /// <summary>Bridges an enum-valued property to a ComboBox's int SelectedIndex (and back) so a
    /// dialog can bind directly to a view model's enum property instead of needing a dedicated
    /// int-mirror property + change handler for every enum dropdown.</summary>
    public class EnumIndexConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is null ? 0 : System.Convert.ToInt32(value);

        // SelectedIndex can transiently become -1 while Avalonia re-applies the ComboBox's
        // control template; Enum.ToObject on an undefined value would otherwise persist a
        // garbage enum value to the workspace file the next time the bound property is saved.
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not int index || !targetType.IsEnum)
            {
                return value;
            }

            var candidate = Enum.ToObject(targetType, index);
            return Enum.IsDefined(targetType, candidate) ? candidate : BindingOperations.DoNothing;
        }
    }
}
