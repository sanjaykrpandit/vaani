using System;
using Avalonia.Data.Converters;
using System.Globalization;

namespace Vaani.Converters;

public class BoolToDoubleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isChecked = value is bool b && b;
        return isChecked ? 20.0 : 0.0;  // Move 20px right when true, 0px when false
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
