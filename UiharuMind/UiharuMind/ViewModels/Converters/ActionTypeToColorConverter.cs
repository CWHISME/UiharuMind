using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace UiharuMind.ViewModels.Converters;

/// <summary>
/// 将动作类型转换为对应的背景颜色
/// </summary>
public class ActionTypeToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            "MouseClick" => new SolidColorBrush(Color.Parse("#DBEAFE")), // 蓝色
            "KeyPress" => new SolidColorBrush(Color.Parse("#E0E7FF")),   // 紫色
            "Delay" => new SolidColorBrush(Color.Parse("#FEF3C7")),      // 黄色
            "Text" => new SolidColorBrush(Color.Parse("#D1FAE5")),       // 绿色
            _ => new SolidColorBrush(Color.Parse("#F3F4F6"))             // 灰色
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
