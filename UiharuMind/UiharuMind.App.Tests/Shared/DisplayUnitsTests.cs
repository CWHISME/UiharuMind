using Avalonia;
using UiharuMind.Shared.Utils;

namespace UiharuMind.App.Tests.Shared;

/// <summary>
/// 钉住全局像素→窗口坐标的映射契约。хгалтору钩子看到的是全局物理像素，
/// 窗口要的是窗内 DIP，中间差着主副屏原点与 backing，推导错一位就是选区错位。
/// </summary>
public class DisplayUnitsTests
{
    // mac 主屏 Retina：point 原点 (0,0)，backing 2，窗 1512x982 铺满
    [Fact]
    public void MapMacPrimaryRetina()
    {
        bool ok = DisplayUnits.TryMapGlobalPixelsToWindow(
            3024, 1964, true, 2.0, 1.0,
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
        bool ok = DisplayUnits.TryMapGlobalPixelsToWindow(
            100, 100, true, 2.0, 1.0,
            new PixelPoint(0, 0), new Size(1512, 982),
            out Point windowDip, out PixelPoint screenUnits);

        Assert.True(ok);
        Assert.Equal(50, windowDip.X);
        Assert.Equal(50, windowDip.Y);
        Assert.Equal(new PixelPoint(50, 50), screenUnits);
    }

    // mac 副屏同缩放：point 原点 (1512,0)，backing 1，像素原点按同布局推算 (1512,0)
    [Fact]
    public void MapMacSecondarySameScale()
    {
        bool ok = DisplayUnits.TryMapGlobalPixelsToWindow(
            2000, 500, false, 1.0, 1.0,
            new PixelPoint(1512, 0), new Size(1920, 1080),
            out Point windowDip, out PixelPoint screenUnits);

        Assert.True(ok);
        Assert.Equal(488, windowDip.X);
        Assert.Equal(500, windowDip.Y);
        Assert.Equal(new PixelPoint(2000, 500), screenUnits);
    }

    // Windows 主屏 150%：Position 是像素，DIP 要除 1.5
    [Fact]
    public void MapWindowsPrimaryScaled()
    {
        bool ok = DisplayUnits.TryMapGlobalPixelsToWindow(
            1920, 1080, true, 1.5, 1.5,
            new PixelPoint(0, 0), new Size(1280, 720),
            out Point windowDip, out PixelPoint screenUnits);

        Assert.True(ok);
        Assert.Equal(1280, windowDip.X);
        Assert.Equal(720, windowDip.Y);
        Assert.Equal(new PixelPoint(1920, 1080), screenUnits);
    }

    // 落到窗外必须拒绝，宁可不用钩子数据
    [Fact]
    public void MapOutsideWindow_ReturnsFalse()
    {
        bool ok = DisplayUnits.TryMapGlobalPixelsToWindow(
            4000, 500, false, 1.0, 1.0,
            new PixelPoint(1512, 0), new Size(1920, 1080),
            out _, out _);

        Assert.False(ok);
    }

    [Fact]
    public void MapInvalidBacking_ReturnsFalse()
    {
        bool ok = DisplayUnits.TryMapGlobalPixelsToWindow(
            100, 100, true, 0, 1.0,
            new PixelPoint(0, 0), new Size(1512, 982),
            out _, out _);

        Assert.False(ok);
    }

    [Fact]
    public void PixelsPerDip_MacTakesRenderScaling()
    {
        // 本机即 mac：Screen.Scaling 恒为 1，必须取传入的 backing。
        // 非 mac 分支在本机测不到，由上面的纯映射用例覆盖两种形状。
        Assert.Equal(2.0, DisplayUnits.PixelsPerDip(1.0, 2.0));
        Assert.Equal(1.0, DisplayUnits.PositionUnitsPerDip(1.0, 2.0));
        Assert.Equal(2.0, DisplayUnits.ScreenBoundsToPixels(1.0, 2.0));
    }
}
