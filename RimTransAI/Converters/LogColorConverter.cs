using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using RimTransAI.Models;

namespace RimTransAI.Converters;

public class LogColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            OperationLogLevel.Success => new SolidColorBrush(Color.Parse("#00C853")),
            OperationLogLevel.Warning => new SolidColorBrush(Color.Parse("#FFA500")),
            OperationLogLevel.Error => new SolidColorBrush(Color.Parse("#FF3B30")),
            _ => new SolidColorBrush(Color.Parse("#CCCCCC"))
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
