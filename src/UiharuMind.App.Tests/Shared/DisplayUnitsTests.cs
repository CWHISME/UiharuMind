using Avalonia;
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
}
