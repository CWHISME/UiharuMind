using System;
using System.Collections.Concurrent;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace UiharuMind.Shared.Converters;

/// <summary>
/// 字号 → 文字的视觉下移量。MainFont（HarmonyOS Sans SC）的中文字形在行盒里偏上：
/// 字体 hhea 的上下沿不对称（字形中心比行盒中心高约 0.04 字号），布局又把行高向上取整、多出的都落在字形下方。
/// 实测 11–24px 中文墨迹中心比行盒中心高约 0.07 字号，和居中排在旁边的图标、圆点对不齐。
/// 字体许可不允许改字体文件，只能在渲染时平移：不参与布局、不改行高。全局样式给所有 TextBlock 挂上，不用再逐个加 tweak
/// </summary>
public sealed class TextOpticalOffsetConverter : IValueConverter
{
    /// <summary>
    /// 下移量与字号之比（实测值，换字体要重量：App.Tests 的 TextOpticalCenterTests）
    /// </summary>
    public const double Ratio = 0.07;

    private static readonly ConcurrentDictionary<double, ITransform> Cache = new();

    /// <summary>
    /// 全局共用的一份
    /// </summary>
    public static TextOpticalOffsetConverter Instance { get; } = new();

    /// <summary>
    /// 某个字号的下移变换（同字号共用一个不可变实例）
    /// </summary>
    /// <param name="fontSize">字号</param>
    /// <returns>平移变换</returns>
    public static ITransform For(double fontSize) =>
        Cache.GetOrAdd(fontSize, size => new ImmutableTransform(Matrix.CreateTranslation(0, Pixels(size))));

    /// <summary>
    /// 下移多少（取整像素：小数平移会让字形落在半像素上发虚）
    /// </summary>
    /// <param name="fontSize">字号</param>
    /// <returns>像素</returns>
    public static double Pixels(double fontSize) => Math.Round(fontSize * Ratio, MidpointRounding.AwayFromZero);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double size && size > 0 ? For(size) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
