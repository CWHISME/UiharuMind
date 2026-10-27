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

namespace UiharuMind.Shared.Utils;

/// <summary>
/// 以光标为锚点的等比缩放几何计算，纯函数、可单测。
/// <para>
/// 契约：缩放只改尺寸，不动「光标底下那个图点」。每格都从<b>当前真实几何</b>反推锚点再算目标位置，
/// 所以既不会累积取整残差，也不依赖上一次算出来的位置。
/// </para>
/// <para>
/// 两个历史坑（都会表现成「越缩越飘」）：
/// 1. 阴影留白 <c>margin</c> 是常量、不随缩放变化，只能<b>加</b>在换算里，不能乘进缩放比率；
/// 2. 锚点比例必须用<b>归一化坐标</b>（0..1）而不是 DIP 偏移量——DIP 偏移本身随缩放变化，
///    拿它乘比率等于把缩放算了两遍。
/// </para>
/// </summary>
public static class ZoomAnchorMath
{
    /// <summary>
    /// 光标在图片内的归一化位置（0..1，左上为原点）。光标不在图片上时钳到最近的边。
    /// </summary>
    /// <param name="cursor">光标位置，单位与窗口 Position 一致（Windows 物理像素、mac point）</param>
    /// <param name="framePosition">窗口左上，单位同上</param>
    /// <param name="margin">图片到窗口边的留白（DIP）</param>
    /// <param name="unitsPerDip">每个 DIP 对应多少 Position 单位</param>
    /// <param name="displaySize">当前图片显示尺寸（DIP，不含留白）</param>
    public static Point NormalizeAnchor(PixelPoint cursor, PixelPoint framePosition, Thickness margin,
        double unitsPerDip, Size displaySize)
    {
        double widthPx = displaySize.Width * unitsPerDip;
        double heightPx = displaySize.Height * unitsPerDip;
        // 尺寸还没落定（首次显示前）时退回中心，至少不会把窗口甩到屏幕外
        double u = widthPx > 0 ? (cursor.X - (framePosition.X + margin.Left * unitsPerDip)) / widthPx : 0.5;
        double v = heightPx > 0 ? (cursor.Y - (framePosition.Y + margin.Top * unitsPerDip)) / heightPx : 0.5;
        return new Point(Math.Clamp(u, 0, 1), Math.Clamp(v, 0, 1));
    }

    /// <summary>
    /// 在给定光标位置与锚点比例下，缩放后窗口左上角应落在的 Position 单位坐标。
    /// </summary>
    /// <param name="cursor">光标位置，单位与窗口 Position 一致</param>
    /// <param name="margin">图片到窗口边的留白（DIP）</param>
    /// <param name="unitsPerDip">每个 DIP 对应多少 Position 单位</param>
    /// <param name="anchor">归一化锚点（见 <see cref="NormalizeAnchor"/>）</param>
    /// <param name="newDisplaySize">缩放后的图片显示尺寸（DIP，不含留白）</param>
    public static PixelPoint PositionForAnchor(PixelPoint cursor, Thickness margin, double unitsPerDip, Point anchor,
        Size newDisplaySize)
    {
        double x = cursor.X - margin.Left * unitsPerDip - anchor.X * newDisplaySize.Width * unitsPerDip;
        double y = cursor.Y - margin.Top * unitsPerDip - anchor.Y * newDisplaySize.Height * unitsPerDip;
        // 落到整像素：只在最后取整一次，中间量保持 double
        return new PixelPoint((int)Math.Round(x), (int)Math.Round(y));
    }
}
