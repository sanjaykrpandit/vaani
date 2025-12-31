using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Vaani.Converters;

public class MessageBubbleColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isFromMeeting)
        {
            // Meeting messages: darker gray, User messages: blue
            return isFromMeeting 
                ? new SolidColorBrush(Color.Parse("#3A3A3C"))
                : new SolidColorBrush(Color.Parse("#292929"));
        }
        return new SolidColorBrush(Color.Parse("#292929"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
