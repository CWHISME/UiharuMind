using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace UiharuMind.Shared.Converters;

/// <summary>
/// 将整数转换为布尔值（0 = false, >0 = true）
/// </summary>
public class IntToBoolConverter : IValueConverter
{
    public static readonly IntToBoolConverter Instance = new();

    /// <summary>取反的那一份（0 = true, &gt;0 = false），用于"空列表时才显示"这类绑定</summary>
    public static readonly IntToBoolConverter Inverted = new() { IsInverted = true };

    /// <summary>是否取反</summary>
    public bool IsInverted { get; init; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool result = value is int intValue && intValue > 0;
        return IsInverted ? !result : result;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
