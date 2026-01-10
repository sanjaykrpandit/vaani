using Avalonia;
using Avalonia.Data.Converters;
using System.Globalization;

public class Width80PercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Rect bounds)
            return bounds.Width * 0.8;

        return AvaloniaProperty.UnsetValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
