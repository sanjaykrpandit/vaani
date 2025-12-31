using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Vaani.DriverInstallation.Converters;

/// <summary>
/// Converts boolean to color for success/error indication
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool hasError)
        {
            // Red for error, Green for success
            return hasError ? Color.FromRgb(220, 50, 50) : Color.FromRgb(50, 200, 50);
        }
        
        return Color.FromRgb(128, 128, 128);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts boolean to success/error icon
/// </summary>
public class BoolToIconConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 3 && values[0] is bool hasError)
        {
            return hasError ? values[2] : values[1]; // Return error or success icon
        }
        
        return "?";
    }
}
