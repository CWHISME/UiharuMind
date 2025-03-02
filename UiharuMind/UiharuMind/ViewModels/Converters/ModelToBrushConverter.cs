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
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Interfaces;
using UiharuMind.Core.AI.Memery;

namespace UiharuMind.ViewModels.Converters;

public class ModelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ILlmModel modelRunningData)
        {
            if (LlmManager.Instance.RemoteModelManager.Config.FavoriteModel == modelRunningData.ModelName)
                return Brushes.Gold;
        }

        if (value is MemoryData memoryData)
        {
            return Brushes.Gold;
        }

        return Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}