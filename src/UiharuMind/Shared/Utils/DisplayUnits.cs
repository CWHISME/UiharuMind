using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
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
    /// 该平台的窗口几何是否只能落在整物理像素上。
    /// Windows/Linux 的 SetWindowPos 只收整数；mac 的 setFrame 收 CGFloat，不需要也不该对齐。
    /// </summary>
    public static bool HasIntegerPixelGeometry => !OperatingSystem.IsMacOS();

    /// <summary>
    /// 某个窗口自己的 DIP→物理像素倍率。窗口尺寸、布局取整、Position 单位换算都以它为唯一口径。
    /// <para>
    /// 优先用 <see cref="TopLevel.RenderScaling"/>：这正是 Avalonia 自己算布局取整
    /// （LayoutHelper.RoundLayoutSizeUp）与 PixelSize.FromSize 用的那个倍率。
    /// Show 之前它还停在 1，此时退回窗口所在屏的 Scaling。
    /// </para>
    /// </summary>
    /// <param name="window">目标窗口</param>
    public static double WindowPixelsPerDip(Window window)
    {
        if (window.IsVisible && window.RenderScaling > 0) return window.RenderScaling;
        return window.Screens.ScreenFromWindow(window)?.Scaling
               ?? window.Screens.Primary?.Scaling
               ?? 1.0;
    }

    /// <summary>
    /// 某个窗口的 Position 单位换算（每 DIP 多少 Position 单位）。mac 恒为 1，其余平台同 <see cref="WindowPixelsPerDip"/>。
    /// </summary>
    /// <param name="window">目标窗口</param>
    public static double WindowPositionUnitsPerDip(Window window)
    {
        return OperatingSystem.IsMacOS() ? 1.0 : WindowPixelsPerDip(window);
    }

    /// <summary>
    /// 把 DIP 尺寸对齐到「窗口能稳定保持」的整物理像素网格，返回对齐后的 DIP；不需要对齐的平台原样返回。
    /// <para>
    /// 目标像素先向上取整（口径见 <see cref="Avalonia.PixelSize.FromSize"/>），再向下收敛到
    /// 托管 resize 能原样保持的像素值。理由：平台后端把 DIP 转像素用的是 <c>(int)(DIP × scale)</c> 截断
    /// （Win32 <c>WindowImpl.Resize</c>、X11 <c>X11Window.ToPixelSize</c> 同款），
    /// 像素 <c>P</c> 若截不回自身（<c>(int)((P/scale)×scale) &lt; P</c>），后续每条改尺寸的路径都会
    /// 二次把窗口改小 1px——原生落盘的尺寸与窗口真实尺寸分叉，逐格缩放就成了抖动。
    /// </para>
    /// <para>
    /// 托管 resize 改尺寸有两条入口，都要过同一个截断判定，收敛因此按两条一起卡（见
    /// <see cref="IsHeldAtPixelSize"/>）。落到稳定像素后，两条路径都在
    /// <c>WindowImpl.Resize</c> 的「client 尺寸已相等」分支直接返回，同一次缩放只提交一次几何。
    /// </para>
    /// <para>
    /// 收敛是幂等的：已对齐的值再对齐一次不会变，也不会每滚一格莫名长胖/缩水。
    /// </para>
    /// </summary>
    /// <param name="dipSize">待对齐的 DIP 尺寸</param>
    /// <param name="pixelsPerDip">每 DIP 多少物理像素，见 <see cref="WindowPixelsPerDip"/></param>
    public static Size SnapToPixels(Size dipSize, double pixelsPerDip)
    {
        if (!HasIntegerPixelGeometry) return dipSize;
        if (pixelsPerDip <= 0 || !double.IsFinite(pixelsPerDip)) return dipSize;
        return new Size(
            AlignUpToPixel(dipSize.Width, pixelsPerDip),
            AlignUpToPixel(dipSize.Height, pixelsPerDip));
    }

    private static double AlignUpToPixel(double dipValue, double pixelsPerDip)
    {
        // 先按 8 位小数收敛浮点误差再 ceil（与 Avalonia 的 RoundTo8Digits 同义），保证幂等
        int pixels = (int)Math.Ceiling(Math.Round(dipValue * pixelsPerDip, 8));
        while (pixels > 1 && !IsHeldAtPixelSize(pixels, pixelsPerDip)) pixels--;
        return pixels / pixelsPerDip;
    }

    /// <summary>
    /// 像素值能否被托管 resize 原样保持。托管改尺寸的两条入口都要过
    /// 后端 <c>(int)(DIP × scale)</c> 的截断判定（Win32/X11 同款），截不回自身就会被二次改小：
    /// 1. Width/Height → 布局圆整（<see cref="LayoutHelper.RoundLayoutValueUp"/>）→ 截断；
    /// 2. 原生改尺寸 → WM_SIZE → ClientSize = 像素/scale 回灌订阅 → 截断。
    /// </summary>
    /// <param name="pixels">候选像素值</param>
    /// <param name="pixelsPerDip">每 DIP 多少物理像素</param>
    private static bool IsHeldAtPixelSize(int pixels, double pixelsPerDip)
    {
        double dip = pixels / pixelsPerDip;
        if ((int)(dip * pixelsPerDip) != pixels) return false;
        double arranged = LayoutHelper.RoundLayoutValueUp(dip, pixelsPerDip);
        return (int)(arranged * pixelsPerDip) == pixels;
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
