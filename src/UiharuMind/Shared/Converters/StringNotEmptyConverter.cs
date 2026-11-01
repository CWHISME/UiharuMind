using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace UiharuMind.Shared.Converters;

/// <summary>
/// 字符串 → 布尔：非空为 true。用来在 XAML 里给「有内容才显示」的行做 <c>IsVisible</c>，
/// 省掉为每个可空文本单开一个 <c>HasXxx</c> 布尔属性
/// </summary>
public class StringNotEmptyConverter : IValueConverter
{
    /// <summary>非空为 true</summary>
    public static readonly StringNotEmptyConverter Instance = new();

    /// <summary>反过来的那一份（空为 true）</summary>
    public static readonly StringNotEmptyConverter Inverted = new() { IsInverted = true };

    /// <summary>是否取反</summary>
    public bool IsInverted { get; init; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool notEmpty = value is string text && text.Length > 0;
        return IsInverted ? !notEmpty : notEmpty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
