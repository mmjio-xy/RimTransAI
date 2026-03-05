using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace RimTransAI.Converters;

public class LogColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string message)
            return new SolidColorBrush(Color.Parse("#CCCCCC"));

        if (message.Contains("开始处理批次"))
            return new SolidColorBrush(Color.Parse("#FFA500"));

        if (message.Contains("完成") || message.Contains("翻译任务全部完成"))
            return new SolidColorBrush(Color.Parse("#00FF00"));

        if (message.Contains("错误") || message.Contains("失败") || message.Contains("✗"))
            return new SolidColorBrush(Color.Parse("#FF0000"));

        return new SolidColorBrush(Color.Parse("#CCCCCC"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
