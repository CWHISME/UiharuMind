using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace UiharuMind.ViewModels.Converters;

public class AddConverter : IValueConverter
{
    public static readonly AddConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double d && parameter is string param && double.TryParse(param, out var sub))
        {
            // 防止减成负数
            return Math.Max(0, d + sub);
        }
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}