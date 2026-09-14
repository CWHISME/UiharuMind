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
    /// 全局钩子坐标转窗内 DIP。
    /// <para>
    /// 钩子给的坐标与 <c>Screen.Bounds</c>/<c>Window.Position</c> 同一个坐标系：Windows/Linux 是
    /// 物理像素，macOS 是 point（libuiohook 走 <c>CGEventGetLocation</c>，拿到的就是 NSScreen 那套
    /// point）。所以只要减掉窗口原点再除以该坐标系每 DIP 多少单位即可，无需按屏推算像素原点。
    /// </para>
    /// <para>
    /// 曾经把它当成物理像素处理，Retina 主屏下钩子坐标会被再除一次 backing，
    /// 得到的是窗口事件坐标的一半——拖选时两个来源交替刷新，选区与 tips 就一直闪。
    /// </para>
    /// 纯函数，可单测。
    /// </summary>
    /// <param name="x">钩子报告的 X（屏幕坐标系）</param>
    /// <param name="y">钩子报告的 Y（屏幕坐标系）</param>
    /// <param name="positionUnitsPerDip">见 <see cref="PositionUnitsPerDip"/></param>
    /// <param name="windowOrigin">窗口原点（Position 口径）</param>
    /// <param name="windowDipSize">窗口内容尺寸（DIP）</param>
    /// <param name="windowDip">窗内 DIP 坐标</param>
    /// <param name="screenUnits">屏幕坐标系坐标（与 Screen.Bounds 同口径）</param>
    /// <returns>落在窗口内、映射成功返回 True</returns>
    public static bool TryMapGlobalPointerToWindow(
        int x, int y, double positionUnitsPerDip,
        PixelPoint windowOrigin, Size windowDipSize,
        out Point windowDip, out PixelPoint screenUnits)
    {
        windowDip = default;
        screenUnits = default;
        if (positionUnitsPerDip <= 0) return false;
        if (windowDipSize.Width <= 0 || windowDipSize.Height <= 0) return false;

        double wx = (x - windowOrigin.X) / positionUnitsPerDip;
        double wy = (y - windowOrigin.Y) / positionUnitsPerDip;

        // 落到窗外（在别的屏上点的）直接拒绝，宁可不用钩子数据，也不引入错误坐标
        if (wx < -2 || wy < -2 || wx > windowDipSize.Width + 2 || wy > windowDipSize.Height + 2) return false;
        windowDip = new Point(wx, wy);
        screenUnits = new PixelPoint(x, y);
        return true;
    }
}
