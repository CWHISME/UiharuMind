using Avalonia;
using UiharuMind.Shared.Utils;

namespace UiharuMind.App.Tests.Shared;

/// <summary>
/// 钉住「以光标为锚点缩放」的几何契约。
/// 这里错一点不会立刻炸，但每滚一格都把鼠标底下的图点挪一点，连滚十几格就是肉眼可见的漂+抖。
/// 三个坑各一条回归：
/// 1. 固定留白不能乘进缩放比率；2. 锚点要用比例而不是 DIP 偏移；3. 逐格不能累积取整残差。
/// </summary>
public class ZoomAnchorMathTests
{
    // 贴图窗的阴影留白（ScreenCapturePreviewWindow.ShadowMargin）
    private static readonly Thickness Margin = new(12, 8, 12, 16);

    [Fact]
    public void NormalizeAnchor_ImageCenter_ReturnsHalf()
    {
        // 400x300 的图，窗口在 (600,300)，150% 缩放：图片原点 = 600+18, 300+12
        var frame = new PixelPoint(600, 300);
        var cursor = new PixelPoint(600 + 18 + 300, 300 + 12 + 225); // 图片正中
        Point anchor = ZoomAnchorMath.NormalizeAnchor(cursor, frame, Margin, 1.5, new Size(400, 300));

        Assert.Equal(0.5, anchor.X, 3);
        Assert.Equal(0.5, anchor.Y, 3);
    }

    [Fact]
    public void NormalizeAnchor_CursorOutsideImage_ClampsToEdge()
    {
        var anchor = ZoomAnchorMath.NormalizeAnchor(new PixelPoint(0, 0), new PixelPoint(600, 300), Margin, 1.5,
            new Size(400, 300));

        Assert.Equal(0.0, anchor.X, 3);
        Assert.Equal(0.0, anchor.Y, 3);
    }

    [Fact]
    public void PositionForAnchor_KeepsAnchorUnderCursor_AfterRescale()
    {
        var cursor = new PixelPoint(768, 432);
        Point anchor = ZoomAnchorMath.NormalizeAnchor(cursor, new PixelPoint(600, 300), Margin, 1.5,
            new Size(400, 300));

        // 放大 10% 之后重新反算锚点，必须还是同一个比例
        var newSize = new Size(440, 330);
        var newPos = ZoomAnchorMath.PositionForAnchor(cursor, Margin, 1.5, anchor, newSize);
        Point again = ZoomAnchorMath.NormalizeAnchor(cursor, newPos, Margin, 1.5, newSize);

        Assert.Equal(anchor.X, again.X, 3);
        Assert.Equal(anchor.Y, again.Y, 3);
    }

    /// <summary>
    /// 回归：固定留白只能加，不能乘进 (zoom-1)。
    /// 曾经写成 pos = cur - (mouse + margin) * units * (zoom - 1)，每格多偏 margin*(zoom-1)，
    /// 12 格攒到 25px（实测），表现就是「缩放时鼠标底下的图一直往一边跑」。
    /// </summary>
    [Fact]
    public void PositionForAnchor_DoesNotScaleMargin()
    {
        var cursor = new PixelPoint(768, 432);
        var current = new Size(400, 300);
        var frame = new PixelPoint(600, 300);
        Point anchor = ZoomAnchorMath.NormalizeAnchor(cursor, frame, Margin, 1.5, current);

        var newSize = new Size(440, 330);
        var pos = ZoomAnchorMath.PositionForAnchor(cursor, Margin, 1.5, anchor, newSize);

        // 图片原点（px）= 位置 + 留白*units；它相对光标的偏移应恰好等于锚点比例 × 新尺寸
        double imageOriginX = pos.X + Margin.Left * 1.5;
        double imageOriginY = pos.Y + Margin.Top * 1.5;
        Assert.Equal(anchor.X * newSize.Width * 1.5, cursor.X - imageOriginX, 1);
        Assert.Equal(anchor.Y * newSize.Height * 1.5, cursor.Y - imageOriginY, 1);
    }

    /// <summary>
    /// 连续缩放手势（锚点锁定，与 ScreenCapturePreviewWindow.ResolveZoomAnchor 一致）：
    /// 漂移必须始终 &lt; 1px，不许随格数增长。
    /// </summary>
    [Fact]
    public void LockedAnchor_DoesNotDriftAcrossManyTicks()
    {
        var cursor = new PixelPoint(768, 432);
        var pos = new PixelPoint(600, 300);
        var size = new Size(400, 300);
        Point anchor = ZoomAnchorMath.NormalizeAnchor(cursor, pos, Margin, 1.5, size);

        for (int i = 0; i < 20; i++)
        {
            size = new Size(size.Width * 1.1, size.Height * 1.1);
            pos = ZoomAnchorMath.PositionForAnchor(cursor, Margin, 1.5, anchor, size);

            Point actual = ZoomAnchorMath.NormalizeAnchor(cursor, pos, Margin, 1.5, size);
            double driftX = (actual.X - anchor.X) * size.Width * 1.5;
            double driftY = (actual.Y - anchor.Y) * size.Height * 1.5;

            Assert.True(Math.Abs(driftX) <= 0.5, $"第 {i + 1} 格 X 漂了 {driftX:F2}px");
            Assert.True(Math.Abs(driftY) <= 0.5, $"第 {i + 1} 格 Y 漂了 {driftY:F2}px");
        }
    }

    /// <summary>
    /// mac 语义回归：unitsPerDip = 1（CGEventGetLocation 与 Window.Position 同为 point）、不做像素对齐。
    /// 在这个前提下新公式必须与原来的 `pos = frame - mouseDip * (zoom - 1)` 逐像素等价——
    /// mac 那边原本就顺，这次改写不许改变它的行为。
    /// </summary>
    [Fact]
    public void MacSemantics_MatchLegacyFormula()
    {
        var frame = new PixelPoint(1000, 400);
        var cursor = new PixelPoint(1300, 560);
        var current = new Size(400, 300);
        const double units = 1.0; // mac：每 DIP 一个 point

        foreach (double zoom in new[] { 0.4, 0.75, 0.9, 1.1, 1.5, 2.7 })
        {
            var newSize = new Size(current.Width * zoom, current.Height * zoom);
            Point anchor = ZoomAnchorMath.NormalizeAnchor(cursor, frame, Margin, units, current);
            var pos = ZoomAnchorMath.PositionForAnchor(cursor, Margin, units, anchor, newSize);

            // 旧式（mac 上原本那条）：mouseDip 是相对 ImageContent 的偏移，留白不在乘法里
            double mouseDipX = cursor.X - frame.X - Margin.Left;
            double mouseDipY = cursor.Y - frame.Y - Margin.Top;
            double legacyX = frame.X - mouseDipX * (zoom - 1);
            double legacyY = frame.Y - mouseDipY * (zoom - 1);

            Assert.True(Math.Abs(pos.X - legacyX) <= 0.51, $"zoom {zoom}: X 与旧式差 {pos.X - legacyX:F2}px");
            Assert.True(Math.Abs(pos.Y - legacyY) <= 0.51, $"zoom {zoom}: Y 与旧式差 {pos.Y - legacyY:F2}px");
        }
    }

    /// <summary>
    /// 同一几何在 100% / 150% / 200% 下算出的窗口位置必须自洽：
    /// units 是「每 DIP 多少 Position 单位」，位置只该按它线性缩放。
    /// </summary>
    [Fact]
    public void PositionForAnchor_ScalesLinearlyWithUnits()
    {
        Point anchor = new(0.25, 0.25);
        var size = new Size(400, 300);

        var at100 = ZoomAnchorMath.PositionForAnchor(new PixelPoint(1000, 800), new Thickness(12, 8), 1.0, anchor,
            size);
        var at200 = ZoomAnchorMath.PositionForAnchor(new PixelPoint(2000, 1600), new Thickness(12, 8), 2.0, anchor,
            size);

        Assert.Equal(at100.X * 2d, at200.X, 1);
        Assert.Equal(at100.Y * 2d, at200.Y, 1);
    }
}
