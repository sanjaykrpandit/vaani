using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Vaani.Converters;

public class BoolToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isMuted)
        {
            // Check if parameter specifies speaker context
            var context = parameter?.ToString()?.ToLowerInvariant() ?? "";
            
            string resourcePath;
            
            if (context.Contains("speaker"))
            {
                // Speaker images
                resourcePath = isMuted ? "avares://vconsole/Assets/mspeaker.png" : "avares://vconsole/Assets/speaker.png";
            }
            else
            {
                // Microphone images
                resourcePath = isMuted ? "avares://vconsole/Assets/mmic.png" : "avares://vconsole/Assets/mic.png";
            }
            
            try
            {
                var uri = new Uri(resourcePath);
                var asset = AssetLoader.Open(uri);
                return new Bitmap(asset);
            }
            catch
            {
                // Fallback if asset loading fails
                return null;
            }
        }
        
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
