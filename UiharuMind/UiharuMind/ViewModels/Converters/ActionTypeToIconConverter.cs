using System;
using System.Globalization;
using Avalonia.Data.Converters;
using UiharuMind.Core.AutoClick;

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
            AutoClickActionType.MouseClick => "🖱",
            AutoClickActionType.MouseDown => "⬇️",
            AutoClickActionType.MouseUp => "⬆️",
            AutoClickActionType.MouseMove => "➡️",
            AutoClickActionType.MouseWheel => "🔄",
            AutoClickActionType.KeyPress => "⌨",
            AutoClickActionType.KeyDown => "⬇️",
            AutoClickActionType.KeyUp => "⬆️",
            AutoClickActionType.Delay => "⏱",
            AutoClickActionType.Text => "📝",
            _ => "❓"
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
