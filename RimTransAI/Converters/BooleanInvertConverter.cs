using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RimTransAI.Converters;

/// <summary>
/// 布尔值取反转换器。
/// </summary>
public class BooleanInvertConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return false;
    }
}
