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
using Microsoft.Extensions.AI;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.ViewModels.Converters;

/// <summary>
/// 如果角色为 User，返回右边，否则返回左边
/// </summary>
public class UserLeftRightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // ChatRole 是 struct 而非枚举，不能用常量模式匹配
        if (value is not ChatRole role) return HorizontalAlignment.Left;
        if (role == ChatRole.System) return HorizontalAlignment.Center;
        if (role == ChatRole.User) return HorizontalAlignment.Right;
        return HorizontalAlignment.Left;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        Log.Error("Not implemented");
        return null;
    }
}