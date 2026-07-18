using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace UiharuMind.ViewModels.Converters;

/// <summary>
/// 将布尔值转换为 double（默认 false=0, true=180），可通过 parameter 指定 "trueAngle:falseAngle"
/// </summary>
public class BoolToDoubleConverter : IValueConverter
{
    public static BoolToDoubleConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isTrue = value is true;
        if (parameter is string param && param.Contains(':'))
        {
            var parts = param.Split(':');
            if (double.TryParse(parts[0], out var trueAngle) &&
                double.TryParse(parts[1], out var falseAngle))
            {
                return isTrue ? trueAngle : falseAngle;
            }
        }
        return isTrue ? 180.0 : 0.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
