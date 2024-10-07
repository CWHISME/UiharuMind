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
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.ViewModels.Converters;

/// <summary>
/// 开始是用于检测 ScrollViewer 的 Extent 是否大于 Viewport 的 Converter
/// 也就是检测 ScrollViewer 功能是否已经生效
/// 不过其实也可以用于检测两个值的比较：比如第一个值是否大于第二个值等等
/// </summary>
public class TwoValueToBooleanConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count != 2)
            return false;

        double compareValue = (double)values[0]!;
        double maxValue = (double)values[1]!;

        return compareValue > maxValue;
    }

    public object[]? ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        Log.Error("Not implemented");
        return null;
    }
}