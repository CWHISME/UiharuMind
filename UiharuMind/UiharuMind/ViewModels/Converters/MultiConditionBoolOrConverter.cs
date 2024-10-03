using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace UiharuMind.ViewModels.Converters;

/// <summary>
/// 任一条件为真则返回 true，否则返回 false
/// </summary>
public class MultiConditionBoolOrConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        foreach (var value in values)
        {
            if (value is true)
            {
                return true;
            }
        }

        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}