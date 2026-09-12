using System;
using Avalonia;
using Avalonia.Platform;

namespace UiharuMind.Shared.Utils;

/// <summary>
/// 跨平台坐标单位换算的唯一口径，各处不要再各自推导。
/// 背景：Avalonia 在 macOS 下全是 point——Screen.Scaling 恒为 1（native Screens.mm 写死），
/// Bounds/Position 也都是 point（NSScreen frame 与 NSWindow frame 直接透出，无换算，
/// 见 WindowBaseImpl.mm 的 GetPosition/SetPosition）；Windows/Linux 下 Position/Bounds
/// 是物理像素、尺寸是 DIP。因此 mac 上物理像素 = point × backing（窗口 RenderScaling），
/// 其余平台物理像素 = DIP × Screen.Scaling。
/// </summary>
public static class DisplayUnits
{
    /// <summary>
    /// DIP 转物理像素的倍率：mac 取窗口 backing（RenderScaling），其余平台取屏 Scaling。
    /// 参数故意用裸 double 而不用 Screen：Screen 是 abstract 且构造受限，裸参数可单测。
    /// </summary>
    /// <param name="screenScaling">Screen.Scaling</param>
    /// <param name="renderScaling">所在窗口的 RenderScaling（backing 因子）</param>
    public static double PixelsPerDip(double screenScaling, double renderScaling)
    {
        return OperatingSystem.IsMacOS() ? renderScaling : screenScaling;
    }

    /// <summary>
    /// Window.Position 的单位换算：mac 的 Position 就是 point（系数恒 1），
    /// 其余平台是物理像素（同 PixelsPerDip）
    /// </summary>
    public static double PositionUnitsPerDip(double screenScaling, double renderScaling)
    {
        return OperatingSystem.IsMacOS() ? 1.0 : PixelsPerDip(screenScaling, renderScaling);
    }

    /// <summary>
    /// Screen.Bounds 转物理像素的倍率：mac 的 Bounds 是 point，其余平台本来就是像素
    /// </summary>
    public static double ScreenBoundsToPixels(double screenScaling, double renderScaling)
    {
        return PixelsPerDip(screenScaling, renderScaling) / screenScaling;
    }

    /// <summary>
    /// 全局物理像素（钩子坐标）转窗内 DIP。主屏精确（像素原点与 point 原点重合）；
    /// 副屏按同缩放布局推算像素原点，落到窗外则失败——宁可不用，不引入错误起点。
    /// 纯函数，可单测。
    /// </summary>
    /// <param name="px">全局物理像素 X</param>
    /// <param name="py">全局物理像素 Y</param>
    /// <param name="isPrimary">目标屏是否主屏</param>
    /// <param name="pixelsPerDip">见 <see cref="PixelsPerDip"/></param>
    /// <param name="positionUnitsPerDip">见 <see cref="PositionUnitsPerDip"/></param>
    /// <param name="windowOrigin">窗口原点（Position 口径）</param>
    /// <param name="windowDipSize">窗口内容尺寸（DIP）</param>
    /// <param name="windowDip">窗内 DIP 坐标</param>
    /// <param name="screenUnits">屏幕坐标系坐标（与 Screen.Bounds 同口径）</param>
    /// <returns>映射成功返回 True</returns>
    public static bool TryMapGlobalPixelsToWindow(
        int px, int py, bool isPrimary,
        double pixelsPerDip, double positionUnitsPerDip,
        PixelPoint windowOrigin, Size windowDipSize,
        out Point windowDip, out PixelPoint screenUnits)
    {
        windowDip = default;
        screenUnits = default;
        if (pixelsPerDip <= 0 || positionUnitsPerDip <= 0) return false;
        if (windowDipSize.Width <= 0 || windowDipSize.Height <= 0) return false;

        // 前提：调用方窗口铺满本屏（窗口原点即屏幕原点）。此时屏内 DIP = 原点 DIP + 像素偏移，
        // 窗内 DIP = 屏内 − 窗口原点 = (P − G0) / ppd，原点相消，只剩像素原点 G0 一个假设：
        // 主屏 G0 = (0,0) 精确；副屏按同缩放布局推算 G0 = 原点 DIP × ppd。
        double originDipX = windowOrigin.X / positionUnitsPerDip;
        double originDipY = windowOrigin.Y / positionUnitsPerDip;
        double pixelOx = isPrimary ? 0 : originDipX * pixelsPerDip;
        double pixelOy = isPrimary ? 0 : originDipY * pixelsPerDip;
        double wx = (px - pixelOx) / pixelsPerDip;
        double wy = (py - pixelOy) / pixelsPerDip;

        if (wx < -2 || wy < -2 || wx > windowDipSize.Width + 2 || wy > windowDipSize.Height + 2) return false;
        windowDip = new Point(wx, wy);
        screenUnits = new PixelPoint(
            windowOrigin.X + (int)Math.Round(wx * positionUnitsPerDip),
            windowOrigin.Y + (int)Math.Round(wy * positionUnitsPerDip));
        return true;
    }
}
