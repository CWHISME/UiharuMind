/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 两段弧的环形进度：<see cref="SecondaryPercent"/> 画淡色那段（含未计入的部分），
/// <see cref="Percent"/> 在上面画实色那段。从 12 点方向顺时针。
///
/// 与 <c>ContextUsagePanel</c> 里那根两段进度条同一口径（见 ADR 0009）：
/// 环短不代表少画一截，两段叠起来才是「还剩多少空间」。
/// 颜色不写死——由样式按 <c>Tag</c> 选（与进度条那几条选择器同构），档位配色因此只有一处定义。
/// </summary>
public class ArcProgressRing : Control
{
    /// <summary>实色那段的角度占比（0~100）</summary>
    public static readonly StyledProperty<double> PercentProperty =
        AvaloniaProperty.Register<ArcProgressRing, double>(nameof(Percent));

    /// <summary>淡色那段的角度占比（0~100）。小于 <see cref="Percent"/> 时按它自己画，不做夹取</summary>
    public static readonly StyledProperty<double> SecondaryPercentProperty =
        AvaloniaProperty.Register<ArcProgressRing, double>(nameof(SecondaryPercent));

    /// <summary>弧的颜色；一般由样式按档位给</summary>
    public static readonly StyledProperty<IBrush?> ArcBrushProperty =
        AvaloniaProperty.Register<ArcProgressRing, IBrush?>(nameof(ArcBrush));

    /// <summary>轨道颜色（整圈底色）。没有轨道衬着，占用低时那一小段弧会像个色点</summary>
    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<ArcProgressRing, IBrush?>(nameof(TrackBrush));

    /// <summary>线宽</summary>
    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<ArcProgressRing, double>(nameof(Thickness), 2.5);

    /// <summary>淡色那段的透明度</summary>
    public static readonly StyledProperty<double> SecondaryOpacityProperty =
        AvaloniaProperty.Register<ArcProgressRing, double>(nameof(SecondaryOpacity), 0.4);

    /// <summary>
    /// 档位键，供样式选色（Normal / Evicting / Truncating）。
    /// <see cref="Control"/> 上没有 Tag，颜色又只该在样式里定义一处，所以单开这个属性
    /// </summary>
    public static readonly StyledProperty<string> StateKeyProperty =
        AvaloniaProperty.Register<ArcProgressRing, string>(nameof(StateKey), "Normal");

    static ArcProgressRing()
    {
        AffectsRender<ArcProgressRing>(PercentProperty, SecondaryPercentProperty, ArcBrushProperty,
            TrackBrushProperty, ThicknessProperty, SecondaryOpacityProperty);
    }

    /// <summary>档位键，供样式选色</summary>
    public string StateKey
    {
        get => GetValue(StateKeyProperty);
        set => SetValue(StateKeyProperty, value);
    }

    /// <summary>实色那段的角度占比（0~100）</summary>
    public double Percent
    {
        get => GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    /// <summary>淡色那段的角度占比（0~100）</summary>
    public double SecondaryPercent
    {
        get => GetValue(SecondaryPercentProperty);
        set => SetValue(SecondaryPercentProperty, value);
    }

    /// <summary>弧的颜色</summary>
    public IBrush? ArcBrush
    {
        get => GetValue(ArcBrushProperty);
        set => SetValue(ArcBrushProperty, value);
    }

    /// <summary>轨道颜色</summary>
    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>线宽</summary>
    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    /// <summary>淡色那段的透明度</summary>
    public double SecondaryOpacity
    {
        get => GetValue(SecondaryOpacityProperty);
        set => SetValue(SecondaryOpacityProperty, value);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        double thickness = Math.Max(1, Thickness);
        double radius = Math.Min(Bounds.Width, Bounds.Height) / 2 - thickness / 2;
        if (radius <= 0) return;

        Point center = new(Bounds.Width / 2, Bounds.Height / 2);
        if (TrackBrush is { } track)
        {
            context.DrawEllipse(null, new Pen(track, thickness), center, radius, radius);
        }

        if (ArcBrush is not { } arc) return;
        DrawArc(context, center, radius, thickness, SecondaryPercent, SecondaryOpacity, arc);
        DrawArc(context, center, radius, thickness, Percent, 1, arc);
    }

    private static void DrawArc(DrawingContext context, Point center, double radius, double thickness,
        double percent, double opacity, IBrush brush)
    {
        double sweep = Math.Clamp(percent, 0, 100) / 100 * 360;
        if (sweep <= 0.1) return;

        Pen pen = new(brush, thickness) { LineCap = PenLineCap.Round };
        // 画满一圈时首尾重合，ArcTo 会退化成一条零长度的弧（什么都看不见）
        if (sweep >= 359.9)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        Point start = OnCircle(center, radius, -90);
        Point end = OnCircle(center, radius, -90 + sweep);
        StreamGeometry geometry = new();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(start, false);
            ctx.ArcTo(end, new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }

        using (context.PushOpacity(opacity))
        {
            context.DrawGeometry(null, pen, geometry);
        }
    }

    private static Point OnCircle(Point center, double radius, double degrees)
    {
        double radians = degrees * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }
}
