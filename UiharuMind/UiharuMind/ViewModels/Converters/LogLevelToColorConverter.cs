using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.ViewModels.Converters;

public class LogLevelToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ELogType level)
        {
            return level switch
            {
                ELogType.Error => Brushes.Red,
                ELogType.Warning => Brushes.Yellow,
                ELogType.Log => Brushes.Gray,
                _ => Brushes.Black,
            };
        }

        return Brushes.Black;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        Log.Error("Not implemented");
        return null;
    }
}