/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2025.02.18
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.AI.Memory;

namespace UiharuMind.Shared.Converters;

public class ModelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isFavorite)
        {
            return isFavorite ? Colors.Gold : Colors.Gray;
        }

        if (value is ILlmModel modelRunningData)
        {
            if (ModelSettingConfig.Current.IsFavorite(modelRunningData.ModelName))
                return Colors.Gold; //Brushes.Gold;
        }

        if (value is MemoryData)
        {
            return Colors.Gold;
        }

        if (value is List<object> list)
        {
            // 检查列表数量是否不为 0
            return list.Count > 0 ? Colors.Gold : Colors.Gray;
        }

        return Colors.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
