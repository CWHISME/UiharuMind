using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace UiharuMind.Shared.Converters;

/// <summary>
/// 整数等于参数时为 true。常驻页签用它按选中序号切换各页内容的可见性
/// </summary>
public class IndexEqualsConverter : IValueConverter
{
    public static readonly IndexEqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int index && int.TryParse(parameter?.ToString(), out int expected) && index == expected;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
