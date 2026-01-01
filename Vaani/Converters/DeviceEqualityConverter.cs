using System;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Vaani.Models;

namespace Vaani.Converters;

public class DeviceEqualityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        // Handle AudioDeviceInfo comparison
        if (value is AudioDeviceInfo selected && parameter is AudioDeviceInfo current)
            return Equals(selected.Id, current.Id);
        
        // Handle HorizontalAlignment for message bubbles
        if (value is bool isFromMeeting && targetType == typeof(HorizontalAlignment))
            return isFromMeeting ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter is AudioDeviceInfo device)
            return device;
        return null;
    }
}
