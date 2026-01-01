using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Vaani.Converters;

public class BoolToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isMuted)
        {
            // Check if parameter specifies speaker context
            var context = parameter?.ToString()?.ToLowerInvariant() ?? "";
            
            if (context.Contains("speaker"))
            {
                // Speaker: Muted = 🔇, Unmuted = 🔊
                return isMuted ? "🔇" : "🔊";
            }

            // Default (Microphone): Muted = 🔇, Unmuted = 🎙
            return isMuted ? "🎙" : "🎙";
        }
        
        return "🎤";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
