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
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.ViewModels.ViewData;

namespace UiharuMind.ViewModels.Converters;

/// <summary>
/// 如果角色为 User，返回参数指定的列索引，否则返回 0
/// </summary>
public class UserColumIndexConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ECharacter.User)
        {
            int.TryParse(parameter as string, out int index);
            return index;
        }

        return 0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        Log.Error("Not implemented");
        return null;
    }
}