using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace UiharuMind.ViewModels.Converters;

public class AutoClickIndentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var level = value is int intValue ? intValue : 0;
        return new Thickness(Math.Max(0, level) * 18, 0, 0, 6);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class AutoClickKindGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value?.ToString();
        if (string.IsNullOrWhiteSpace(text)) return "?";
        return text[..Math.Min(1, text.Length)].ToUpperInvariant();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
