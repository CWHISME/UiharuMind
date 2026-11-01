using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.Usage;

/// <summary>
/// 图表取色。<b>颜色只在这里出现</b>——XAML 与 series 构造处一律写字面量以外的引用。
///
/// <para>
/// 两套色按 <see cref="ApplicationThemeManager.IsDarkTheme()"/> 选，键取 Semi 与
/// <c>ThemeColors.axaml</c> 里的既有资源（SemiBlue5 / SemiGreen5 / SemiGrey2 / SemiGrey5 /
/// MessageStatus*），所以主题里的色值一改，图表跟着变。取不到资源时（设计器、极早期）退回
/// 同名的浅色值，不抛。
/// </para>
/// </summary>
public sealed class UsageChartPalette
{
    private const string TokenKey = "SemiBlue5";
    private const string CostKey = "SemiGreen5";
    private const string WarningKey = "MessageStatusWarningBrush";
    private const string DangerKey = "MessageStatusErrorBrush";
    private const string GreyKey = "SemiGrey5";
    private const string LineKey = "SemiGrey2";
    private const string PieAccentKey = "SemiBlue3";

    /// <summary>token 柱（主色）</summary>
    public SolidColorPaint TokenFill { get; }

    /// <summary>费用线</summary>
    public SolidColorPaint CostStroke { get; }

    /// <summary>平均延迟线</summary>
    public SolidColorPaint LatencyStroke { get; }

    /// <summary>错误率线</summary>
    public SolidColorPaint ErrorStroke { get; }

    /// <summary>会话命中率线</summary>
    public SolidColorPaint HitRateStroke { get; }

    /// <summary>刻度文字</summary>
    public SolidColorPaint AxisLabel { get; }

    /// <summary>网格线</summary>
    public SolidColorPaint Separator { get; }

    /// <summary>「其他」那一档</summary>
    public SolidColorPaint Muted { get; }

    /// <summary>占比图的取色序列（Top5 + 其他）</summary>
    public IReadOnlyList<SolidColorPaint> Series { get; }

    private static UsageChartPalette? _light; //浅色一套
    private static UsageChartPalette? _dark; //深色一套

    /// <summary>当前主题的那一套</summary>
    public static UsageChartPalette Current =>
        ApplicationThemeManager.IsDarkTheme() ? Dark : Light;

    /// <summary>浅色</summary>
    public static UsageChartPalette Light => _light ??= Build(ThemeVariant.Light);

    /// <summary>深色</summary>
    public static UsageChartPalette Dark => _dark ??= Build(ThemeVariant.Dark);

    private UsageChartPalette(SKColor token, SKColor cost, SKColor warning, SKColor danger, SKColor grey,
        SKColor line, SKColor accent)
    {
        TokenFill = Fill(token);
        CostStroke = Stroke(cost);
        LatencyStroke = Stroke(warning);
        ErrorStroke = Stroke(danger);
        HitRateStroke = Stroke(cost);
        AxisLabel = Fill(grey);
        Separator = Stroke(line, 1f);
        Muted = Fill(grey);
        Series =
        [
            Fill(token), Fill(cost), Fill(accent), Fill(grey), Fill(line),
        ];
    }

    private static UsageChartPalette Build(ThemeVariant variant)
    {
        return new UsageChartPalette(
            Resolve(TokenKey, variant, SKColor.Parse("#0064FA")),
            Resolve(CostKey, variant, SKColor.Parse("#00B42A")),
            Resolve(WarningKey, variant, SKColor.Parse("#B54708")),
            Resolve(DangerKey, variant, SKColor.Parse("#D92D20")),
            Resolve(GreyKey, variant, SKColor.Parse("#86909C")),
            Resolve(LineKey, variant, SKColor.Parse("#E5E6EB")),
            Resolve(PieAccentKey, variant, SKColor.Parse("#6AA1FF")));
    }

    private static SKColor Resolve(string key, ThemeVariant variant, SKColor fallback)
    {
        if (Application.Current is not { } app) return fallback;
        if (!app.TryFindResource(key, variant, out object? value)) return fallback;
        return value switch
        {
            ISolidColorBrush brush => new SKColor(brush.Color.R, brush.Color.G, brush.Color.B, brush.Color.A),
            Color color => new SKColor(color.R, color.G, color.B, color.A),
            _ => fallback,
        };
    }

    private static SolidColorPaint Fill(SKColor color) => new(color);

    private static SolidColorPaint Stroke(SKColor color, float thickness = 2f) => new(color) { StrokeThickness = thickness };
}
