using System;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace Vaani.Converters;

public class BoolToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isRunning = value is bool b && b;
        var param = parameter as string;
        
        // Handle toggle switch colors
        if (param == "toggle")
        {
            return isRunning 
                ? new SolidColorBrush(Color.Parse("#4CAF50"))  // Green when ON
                : new SolidColorBrush(Color.Parse("#3E3E42")); // Gray when OFF
        }
        
        // Original behavior for other cases
        return isRunning ? new SolidColorBrush(Color.Parse("#E74C3C")) : new SolidColorBrush(Color.Parse("#000000"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}

public class BoolToBrushBtnConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isMuted = value is bool b && b;
        // When muted: Red background, When unmuted: Semi-transparent white
        return isMuted 
            ? new SolidColorBrush(Color.Parse("#000000"))      // Red when muted
            : new SolidColorBrush(Color.Parse("#000000"));   // Semi-transparent white when unmuted
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}