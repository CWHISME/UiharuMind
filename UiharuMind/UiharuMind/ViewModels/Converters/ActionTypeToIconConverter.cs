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
            AutoClickStepKind.MouseClick => "M",
            AutoClickStepKind.MouseDown => "↓",
            AutoClickStepKind.MouseUp => "↑",
            AutoClickStepKind.MouseMove => "→",
            AutoClickStepKind.MouseWheel => "W",
            AutoClickStepKind.KeyClick => "K",
            AutoClickStepKind.KeyDown => "↓",
            AutoClickStepKind.KeyUp => "↑",
            AutoClickStepKind.Delay => "T",
            AutoClickStepKind.Text => "A",
            AutoClickStepKind.Loop => "L",
            _ => "?"
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
