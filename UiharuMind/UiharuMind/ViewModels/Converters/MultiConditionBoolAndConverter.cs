using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace UiharuMind.ViewModels.Converters;

/// <summary>
/// 所有条件都满足时返回true，否则返回false
/// </summary>
public class MultiConditionBoolAndConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        foreach (var value in values)
        {
            if (value is false)
            {
                return false;
            }
        }

        return true;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}