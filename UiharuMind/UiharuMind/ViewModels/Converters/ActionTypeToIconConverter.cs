using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace UiharuMind.ViewModels.Converters;

/// <summary>
/// 将动作类型转换为对应的图标
/// </summary>
public class ActionTypeToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            "MouseClick" => "🖱",
            "KeyPress" => "⌨",
            "Delay" => "⏱",
            "Text" => "📝",
            _ => "❓"
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
