using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Vaani.Converters;

public class BoolToConnectionTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isActive)
        {
            return isActive ? "" : "No Audio";
        }
        return "No Audio";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}