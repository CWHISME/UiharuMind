using Avalonia;
using Avalonia.Layout;
using UiharuMind.Shared.Utils;

namespace UiharuMind.App.Tests.Shared;

/// <summary>
/// 钉住全局钩子坐标→窗内 DIP 的映射契约。钩子与 Screen.Bounds/Window.Position 同一个坐标系
/// （Windows/Linux 物理像素、macOS point），窗口要的是窗内 DIP，中间只差窗口原点与每 DIP 的单位数；
/// 这里推导错一位，拖选时选区就跟手错位、两个输入源交替刷新还会一直抖。
/// </summary>
public class DisplayUnitsTests
{
    // mac 主屏 Retina：钩子给的是 point，与窗口事件同口径，不许再除 backing
    [Fact]
    public void MapMacPrimaryRetina_BottomRight()
    {
        bool ok = DisplayUnits.TryMapGlobalPointerToWindow(
            1512, 982, 1.0,
            new PixelPoint(0, 0), new Size(1512, 982),
            out Point windowDip, out PixelPoint screenUnits);

        Assert.True(ok);
        Assert.Equal(1512, windowDip.X);
        Assert.Equal(982, windowDip.Y);
        Assert.Equal(new PixelPoint(1512, 982), screenUnits);
    }

    [Fact]
    public void MapMacPrimaryRetina_Interior()
    {
        bool ok = DisplayUnits.TryMapGlobalPointerToWindow(
            100, 100, 1.0,
            new PixelPoint(0, 0), new Size(1512, 982),
            out Point windowDip, out PixelPoint screenUnits);

        Assert.True(ok);
        Assert.Equal(100, windowDip.X);
        Assert.Equal(100, windowDip.Y);
        Assert.Equal(new PixelPoint(100, 100), screenUnits);
    }

    // mac 副屏：point 原点 (1512,0)，减掉窗口原点即窗内 DIP，不需要按屏推算像素原点
    [Fact]
    public void MapMacSecondary()
    {
        bool ok = DisplayUnits.TryMapGlobalPointerToWindow(
            2000, 500, 1.0,
            new PixelPoint(1512, 0), new Size(1920, 1080),
            out Point windowDip, out PixelPoint screenUnits);

        Assert.True(ok);
        Assert.Equal(488, windowDip.X);
        Assert.Equal(500, windowDip.Y);
        Assert.Equal(new PixelPoint(2000, 500), screenUnits);
    }

    // Windows 主屏 150%：钩子与 Position 都是像素，DIP 要除 1.5
    [Fact]
    public void MapWindowsPrimaryScaled()
    {
        bool ok = DisplayUnits.TryMapGlobalPointerToWindow(
            1920, 1080, 1.5,
            new PixelPoint(0, 0), new Size(1280, 720),
            out Point windowDip, out PixelPoint screenUnits);

        Assert.True(ok);
        Assert.Equal(1280, windowDip.X);
        Assert.Equal(720, windowDip.Y);
        Assert.Equal(new PixelPoint(1920, 1080), screenUnits);
    }

    // Windows 副屏 150%：窗口原点也是像素，先减后除
    [Fact]
    public void MapWindowsSecondaryScaled()
    {
        bool ok = DisplayUnits.TryMapGlobalPointerToWindow(
            2520, 300, 1.5,
            new PixelPoint(1920, 0), new Size(1280, 720),
            out Point windowDip, out PixelPoint screenUnits);

        Assert.True(ok);
        Assert.Equal(400, windowDip.X);
        Assert.Equal(200, windowDip.Y);
        Assert.Equal(new PixelPoint(2520, 300), screenUnits);
    }

    // 落到窗外必须拒绝，宁可不用钩子数据
    [Fact]
    public void MapOutsideWindow_ReturnsFalse()
    {
        bool ok = DisplayUnits.TryMapGlobalPointerToWindow(
            4000, 500, 1.0,
            new PixelPoint(1512, 0), new Size(1920, 1080),
            out _, out _);

        Assert.False(ok);
    }

    [Fact]
    public void MapInvalidUnits_ReturnsFalse()
    {
        bool ok = DisplayUnits.TryMapGlobalPointerToWindow(
            100, 100, 0,
            new PixelPoint(0, 0), new Size(1512, 982),
            out _, out _);

        Assert.False(ok);
    }

    [Fact]
    public void PixelsPerDip_MacTakesRenderScaling()
    {
        // 这条验证的是 mac 分支(IsMacOS 时取 renderScaling)。CI 跑在 Linux 上,
        // 非 mac 分支在这里不适用,跳过——两种形状由上面的纯映射用例覆盖。
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Skip("验证 mac 的 Screen.Scaling 恒 1 假设,非 mac 平台跳过");
        }

        Assert.Equal(2.0, DisplayUnits.PixelsPerDip(1.0, 2.0));
        Assert.Equal(1.0, DisplayUnits.PositionUnitsPerDip(1.0, 2.0));
        Assert.Equal(2.0, DisplayUnits.ScreenBoundsToPixels(1.0, 2.0));
    }

    // 对齐的目标不是「ceil 后的任意整像素」，而是托管 resize 能原样保持的像素：
    // 平台后端把 DIP 转像素用 (int)(DIP × scale) 截断（Win32 WindowImpl.Resize、X11 同款），
    // 截不回自身的像素会被随后的托管 resize 二次改小 1px，窗口真实尺寸与记账分叉，逐格缩放就是抖动。
    // 两条回灌路径都要过同一个截断判定（见 DisplayUnits.IsHeldAtPixelSize）：
    // 1. 原生改尺寸 → WM_SIZE → ClientSize = 像素/scale 回灌；
    // 2. Width/Height → 布局圆整（RoundLayoutValueUp）→ 截断。
    [Theory]
    [InlineData(400, 1.0)]
    [InlineData(400.3, 1.0)]
    [InlineData(400.3, 1.5)]
    [InlineData(585.64, 1.5)]
    [InlineData(779.48, 1.25)]
    [InlineData(137.3, 1.75)]
    [InlineData(503.7, 1.4)]
    [InlineData(611.2, 1.8)]
    [InlineData(233.33, 1.3)]
    [InlineData(999.99, 1.1)]
    [InlineData(1200.5, 2.25)]
    public void SnapToPixels_LandsOnHeldPixelSize(double dip, double scaling)
    {
        if (OperatingSystem.IsMacOS()) return; // mac 的 setFrame 收 CGFloat，不参与对齐

        Size snapped = DisplayUnits.SnapToPixels(new Size(dip, dip), scaling);
        int px = (int)Math.Round(snapped.Width * scaling);
        Assert.Equal(px, (int)Math.Round(snapped.Height * scaling));

        // 路径 1：原生尺寸回灌的 ClientSize = px/scale，截断必须截回自身
        Assert.Equal(px, (int)((px / scaling) * scaling));
        // 路径 2：Width/Height 先过布局圆整再截断，同样必须截回自身
        Assert.Equal(px, (int)(LayoutHelper.RoundLayoutValueUp(snapped.Width, scaling) * scaling));
    }

    // 收敛只能往下走（不许长过理想尺寸，避免顶穿 MaxWidth 之类的钳制），且最多只让出几个像素
    [Theory]
    [InlineData(400.3, 1.5)]
    [InlineData(585.64, 1.5)]
    [InlineData(137.3, 1.75)]
    [InlineData(503.7, 1.4)]
    [InlineData(611.2, 1.8)]
    [InlineData(233.33, 1.3)]
    [InlineData(999.99, 1.1)]
    public void SnapToPixels_StaysCloseToIdealCeil(double dip, double scaling)
    {
        if (OperatingSystem.IsMacOS()) return;

        Size snapped = DisplayUnits.SnapToPixels(new Size(dip, dip), scaling);
        int ideal = (int)Math.Ceiling(dip * scaling);
        int px = (int)Math.Round(snapped.Width * scaling);

        Assert.InRange(px, ideal - 3, ideal);
    }

    // 尺寸记账每一格都会再对齐一次，必须幂等：否则每滚一格就莫名长胖 1px，缩放会失控
    [Theory]
    [InlineData(586.0, 1.5)]
    [InlineData(400.8, 1.25)]
    [InlineData(532.4, 1.5)]
    [InlineData(1141.24, 1.5)]
    public void SnapToPixels_IsIdempotent(double dip, double scaling)
    {
        if (OperatingSystem.IsMacOS()) return;

        Size once = DisplayUnits.SnapToPixels(new Size(dip, dip), scaling);
        Size twice = DisplayUnits.SnapToPixels(once, scaling);

        Assert.Equal(once, twice);
    }

    // mac 的 NSWindow frame 是 CGFloat，对齐反而会引入平台特有的尺寸台阶
    [Fact]
    public void SnapToPixels_LeavesMacAlone()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("验证 mac 不做像素对齐,非 mac 平台跳过");

        Size size = new(400.3, 300.7);

        Assert.Equal(size, DisplayUnits.SnapToPixels(size, 2.0));
    }
}
