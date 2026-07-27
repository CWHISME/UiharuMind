using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace UiharuMind.ViewModels.Converters;

public class BoolToIconConverter : IValueConverter
{
    public static readonly BoolToIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isContent)
        {
            return isContent ? "file-text" : "search";
        }
        return "search";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
