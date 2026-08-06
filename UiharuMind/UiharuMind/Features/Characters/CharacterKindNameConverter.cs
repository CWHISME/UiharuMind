using System;
using System.Globalization;
using Avalonia.Data.Converters;
using UiharuMind.Core.AI.Character;

namespace UiharuMind.Features.Characters;

/// <summary>
/// 把 <see cref="ECharacterKind"/> 显示为档位名(档位选择器的下拉项用)
/// </summary>
public class CharacterKindNameConverter : IValueConverter
{
    public static readonly CharacterKindNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is ECharacterKind kind ? CharacterKindPresentation.NameOf(kind) : value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
