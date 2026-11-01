using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using SkiaSharp;

namespace UiharuMind.Features.Models.Usage;

/// <summary>
/// LiveCharts 的一次性装配：Skia 渲染器 + 默认映射 + <b>中文 typeface</b>。
///
/// <para>
/// 中文 typeface 不能省：不注册的话刻度与图例里的中文是豆腐块。
/// <c>HasGlobalSKTypeface</c> 在 2.1 系列已 obsolete，这里用新的
/// <see cref="TextSettings.DefaultTypeface"/>（编译器的迁移建议）。
/// </para>
/// <para>
/// <b>不调 <c>AddDefaultTheme()</c></b>：它按 OS 主题自动切，而本仓主题是
/// <c>ApplicationThemeManager.ApplyTheme</c> 手动设的 <c>RequestedThemeVariant</c>，
/// 两者可能不一致。颜色一律由 <see cref="UsageChartPalette"/> 给。
/// </para>
/// <para>
/// 幂等：启动路径与应用测试都会调它，重复调用无副作用
/// </para>
/// </summary>
public static class UsageChartSetup
{
    private static readonly object Locker = new();
    private static bool _configured;

    /// <summary>
    /// 装配一次 LiveCharts（幂等）
    /// </summary>
    public static void EnsureConfigured()
    {
        if (_configured) return;
        lock (Locker)
        {
            if (_configured) return;
            LiveCharts.Configure(config => config
                .AddSkiaSharp()
                .AddDefaultMappers()
                .HasTextSettings(new TextSettings { DefaultTypeface = ResolveCjkTypeface() }));
            _configured = true;
        }
    }

    //系统里找得到汉字字形的那一版字体；找不到就让 LiveCharts 用它自己的默认（英文仍正常）
    private static SKTypeface? ResolveCjkTypeface() => SKFontManager.Default.MatchCharacter('汉');
}
