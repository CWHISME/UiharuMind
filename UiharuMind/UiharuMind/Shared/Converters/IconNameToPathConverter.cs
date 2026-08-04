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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace UiharuMind.Shared.Shell;

/// <summary>
/// 将图标名称转换为 StreamGeometry 的转换器
/// 从 Icons.axaml 的资源中读取图标数据
/// </summary>
public class IconNameToPathConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string iconName || string.IsNullOrWhiteSpace(iconName))
        {
            return AvaloniaProperty.UnsetValue;
        }

        // 尝试从应用程序资源中查找图标
        if (Application.Current?.FindResource(iconName) is StreamGeometry geometry)
        {
            return geometry;
        }

        // 如果找不到,返回 UnsetValue
        return AvaloniaProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}
