using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RimTransAI.Converters;

public class LogLineSplitterConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string logText || string.IsNullOrWhiteSpace(logText))
            return Array.Empty<string>();

        return logText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
