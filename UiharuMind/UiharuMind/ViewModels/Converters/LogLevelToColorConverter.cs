/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

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
                ELogType.Warning => Brushes.Orange,
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