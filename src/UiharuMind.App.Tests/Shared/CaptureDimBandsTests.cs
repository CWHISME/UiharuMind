using Avalonia;
using UiharuMind.Features.ScreenCapture.Overlay;

namespace UiharuMind.App.Tests.Shared;

/// <summary>
/// 钉住截图遮罩暗层的四条带子：合起来必须恰好等于窗口减去选区那块洞，
/// 少一条边或者算出负尺寸，用户看到的就是选区旁边一道亮缝。
/// </summary>
public class CaptureDimBandsTests
{
    private static readonly Size Window = new(1512, 982);

    [Fact]
    public void NoHole_CoversWholeWindow()
    {
        var bands = CaptureDimBands.Calculate(Window, default);

        Assert.Equal(new Rect(0, 0, 1512, 982), bands.Bottom);
        Assert.Equal(0, TotalArea(bands) - 1512 * 982);
    }

    [Fact]
    public void HoleInMiddle_FourBandsTileTheRest()
    {
        var hole = new Rect(100, 200, 300, 400);
        var bands = CaptureDimBands.Calculate(Window, hole);

        Assert.Equal(new Rect(0, 0, 1512, 200), bands.Top);
        Assert.Equal(new Rect(0, 600, 1512, 382), bands.Bottom);
        Assert.Equal(new Rect(0, 200, 100, 400), bands.Left);
        Assert.Equal(new Rect(400, 200, 1112, 400), bands.Right);
        Assert.Equal(1512 * 982 - hole.Width * hole.Height, TotalArea(bands));
    }

    // 贴着右下角拖出窗外：夹到窗口内，不许出现负尺寸
    [Fact]
    public void HoleBeyondWindow_ClampedWithoutNegativeSize()
    {
        var bands = CaptureDimBands.Calculate(Window, new Rect(1400, 900, 500, 500));

        Assert.Equal(new Rect(1512, 900, 0, 82), bands.Right);
        Assert.Equal(new Rect(0, 982, 1512, 0), bands.Bottom);
        Assert.Equal(1512 * 982 - 112 * 82, TotalArea(bands));
    }

    // 几何还没落位时窗口尺寸是 NaN，不能把 NaN 带进布局
    [Fact]
    public void NanWindow_FallsBackToEmpty()
    {
        var bands = CaptureDimBands.Calculate(new Size(double.NaN, double.NaN), default);

        Assert.Equal(0, TotalArea(bands));
    }

    private static double TotalArea(CaptureDimBands bands)
    {
        return bands.Top.Width * bands.Top.Height
               + bands.Bottom.Width * bands.Bottom.Height
               + bands.Left.Width * bands.Left.Height
               + bands.Right.Width * bands.Right.Height;
    }
}
