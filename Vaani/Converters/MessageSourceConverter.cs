using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Vaani.Converters
{
    public class MessageSourceConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count >= 2 &&
                values[0] is bool isSystemMessage &&
                values[1] is bool isFromMeeting)
            {
                if (isSystemMessage)
                    return "Vaani";

                return isFromMeeting ? "Meeting" : "You";
            }

            return "Unknown";
        }

        public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}