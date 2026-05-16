using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using UiharuMind.Core.AutoClick;

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
            AutoClickActionType.MouseClick => new SolidColorBrush(Color.Parse("#DBEAFE")), // 蓝色
            AutoClickActionType.MouseDown => new SolidColorBrush(Color.Parse("#BFDBFE")),  // 深蓝色
            AutoClickActionType.MouseUp => new SolidColorBrush(Color.Parse("#BFDBFE")),    // 深蓝色
            AutoClickActionType.MouseMove => new SolidColorBrush(Color.Parse("#C7D2FE")),  // 紫蓝色
            AutoClickActionType.MouseWheel => new SolidColorBrush(Color.Parse("#DDD6FE")), // 紫色
            AutoClickActionType.KeyPress => new SolidColorBrush(Color.Parse("#E0E7FF")),   // 浅紫色
            AutoClickActionType.KeyDown => new SolidColorBrush(Color.Parse("#C4B5FD")),    // 深紫色
            AutoClickActionType.KeyUp => new SolidColorBrush(Color.Parse("#C4B5FD")),      // 深紫色
            AutoClickActionType.Delay => new SolidColorBrush(Color.Parse("#FEF3C7")),      // 黄色
            AutoClickActionType.Text => new SolidColorBrush(Color.Parse("#D1FAE5")),       // 绿色
            _ => new SolidColorBrush(Color.Parse("#F3F4F6"))                               // 灰色
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
