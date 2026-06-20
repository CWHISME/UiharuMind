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
            AutoClickStepKind.MouseClick => new SolidColorBrush(Color.Parse("#DBEAFE")),
            AutoClickStepKind.MouseDown => new SolidColorBrush(Color.Parse("#BFDBFE")),
            AutoClickStepKind.MouseUp => new SolidColorBrush(Color.Parse("#BFDBFE")),
            AutoClickStepKind.MouseMove => new SolidColorBrush(Color.Parse("#C7D2FE")),
            AutoClickStepKind.MouseWheel => new SolidColorBrush(Color.Parse("#DDD6FE")),
            AutoClickStepKind.KeyClick => new SolidColorBrush(Color.Parse("#E0E7FF")),
            AutoClickStepKind.KeyDown => new SolidColorBrush(Color.Parse("#C4B5FD")),
            AutoClickStepKind.KeyUp => new SolidColorBrush(Color.Parse("#C4B5FD")),
            AutoClickStepKind.Delay => new SolidColorBrush(Color.Parse("#FEF3C7")),
            AutoClickStepKind.Text => new SolidColorBrush(Color.Parse("#D1FAE5")),
            AutoClickStepKind.Loop => new SolidColorBrush(Color.Parse("#CCFBF1")),
            _ => new SolidColorBrush(Color.Parse("#F3F4F6"))
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
