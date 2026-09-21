using Avalonia;
using UiharuMind.Features.ScreenCapture.Overlay;

namespace UiharuMind.App.Tests.Features.ScreenCapture;

/// <summary>
/// 钉住调整模式选框的纯几何：命中测试、平移/缩放钳制。
/// 这些是「松手不截图、回车确认」的核心手感，算错一格选区就描歪。
/// </summary>
public class SelectionGeometryTests
{
    private static readonly Size Window = new(1000, 800);
    private static readonly Rect Box = new(100, 100, 200, 150);

    [Fact]
    public void HitTest_CornersWinOverEdges()
    {
        Assert.Equal(SelectionResizeHandle.TopLeft, SelectionGeometry.HitTest(Box, new Point(100, 100)));
        Assert.Equal(SelectionResizeHandle.TopRight, SelectionGeometry.HitTest(Box, new Point(300, 100)));
        Assert.Equal(SelectionResizeHandle.BottomLeft, SelectionGeometry.HitTest(Box, new Point(100, 250)));
        Assert.Equal(SelectionResizeHandle.BottomRight, SelectionGeometry.HitTest(Box, new Point(300, 250)));
    }

    [Fact]
    public void HitTest_Edges()
    {
        Assert.Equal(SelectionResizeHandle.Top, SelectionGeometry.HitTest(Box, new Point(200, 100)));
        Assert.Equal(SelectionResizeHandle.Bottom, SelectionGeometry.HitTest(Box, new Point(200, 250)));
        Assert.Equal(SelectionResizeHandle.Left, SelectionGeometry.HitTest(Box, new Point(100, 200)));
        Assert.Equal(SelectionResizeHandle.Right, SelectionGeometry.HitTest(Box, new Point(300, 200)));
    }

    [Fact]
    public void HitTest_InsideIsMove()
    {
        Assert.Equal(SelectionResizeHandle.Move, SelectionGeometry.HitTest(Box, new Point(200, 200)));
    }

    [Fact]
    public void HitTest_OutsideIsNone()
    {
        Assert.Equal(SelectionResizeHandle.None, SelectionGeometry.HitTest(Box, new Point(50, 50)));
        Assert.Equal(SelectionResizeHandle.None, SelectionGeometry.HitTest(Box, new Point(500, 500)));
    }

    [Fact]
    public void HitTest_DegenerateBoxIsNone()
    {
        Assert.Equal(SelectionResizeHandle.None, SelectionGeometry.HitTest(default, new Point(10, 10)));
    }

    [Fact]
    public void Move_KeepsSize()
    {
        var next = SelectionGeometry.MoveTo(Box, new Point(400, 300), Window);

        Assert.Equal(new Rect(400, 300, 200, 150), next);
    }

    [Fact]
    public void Move_ClampsAtLeftTop()
    {
        var next = SelectionGeometry.MoveTo(Box, new Point(-50, -30), Window);

        Assert.Equal(new Rect(0, 0, 200, 150), next);
    }

    [Fact]
    public void Move_ClampsAtRightBottom()
    {
        var next = SelectionGeometry.MoveTo(Box, new Point(2000, 2000), Window);

        Assert.Equal(new Rect(800, 650, 200, 150), next);
    }

    [Fact]
    public void Resize_RightEdgeMovesRightOnly()
    {
        var next = SelectionGeometry.ResizeTo(Box, SelectionResizeHandle.Right, new Point(400, 9999), Window);

        Assert.Equal(new Rect(100, 100, 300, 150), next);
    }

    [Fact]
    public void Resize_ShrinksNoSmallerThanMinSide()
    {
        var next = SelectionGeometry.ResizeTo(Box, SelectionResizeHandle.Right, new Point(50, 0), Window);

        Assert.Equal(new Rect(100, 100, SelectionGeometry.MinSideLength, 150), next);
    }

    [Fact]
    public void Resize_ClampsToWindow()
    {
        var next = SelectionGeometry.ResizeTo(Box, SelectionResizeHandle.BottomRight, new Point(9999, 9999), Window);

        Assert.Equal(new Rect(100, 100, 900, 700), next);
    }

    [Fact]
    public void Resize_AnchorSideStaysPut()
    {
        var next = SelectionGeometry.ResizeTo(Box, SelectionResizeHandle.TopLeft, new Point(50, 60), Window);

        Assert.Equal(new Rect(50, 60, 250, 190), next);
    }

    [Fact]
    public void Resize_MoveAndNoneKeepBox()
    {
        Assert.Equal(Box, SelectionGeometry.ResizeTo(Box, SelectionResizeHandle.None, new Point(0, 0), Window));
        Assert.Equal(Box, SelectionGeometry.ResizeTo(Box, SelectionResizeHandle.Move, new Point(0, 0), Window));
    }

    // 暗带是独立四个矩形：Aliased 下各自的非整数边界会各自 round，洞的显示边界必须落在设备像素上
    [Fact]
    public void AlignToDevicePixels_OneX_BoundariesBecomeIntegers()
    {
        var r = SelectionGeometry.AlignToDevicePixels(new Rect(100.4, 150.5, 200.6, 150.4), 1.0);

        Assert.Equal(100.0, r.Left * 1.0, 6);
        Assert.Equal(150.0, r.Top * 1.0, 6);
        Assert.Equal(301.0, r.Right * 1.0, 6);   // 100.4+200.6=301.0
        Assert.Equal(301.0, r.Bottom * 1.0, 6);  // 150.5+150.4=300.9 → round 301
    }

    [Fact]
    public void AlignToDevicePixels_TwoX_SnapsHalfDip()
    {
        // 边界乘回设备像素必须是整数：150.75 DIP @2x = 301.5 设备像素 → round 到 302 → 151 DIP
        var r = SelectionGeometry.AlignToDevicePixels(new Rect(100.25, 150.75, 50.5, 40.25), 2.0);

        Assert.Equal(200.0, r.Left * 2.0, 6);
        Assert.Equal(302.0, r.Top * 2.0, 6);
        Assert.Equal(302.0, r.Right * 2.0, 6);
        Assert.Equal(382.0, r.Bottom * 2.0, 6);
    }

    [Fact]
    public void AlignToDevicePixels_InvalidScale_KeepsBox()
    {
        var box = new Rect(10.5, 20.5, 30.5, 40.5);
        Assert.Equal(box, SelectionGeometry.AlignToDevicePixels(box, 0));
        Assert.Equal(box, SelectionGeometry.AlignToDevicePixels(box, double.NaN));
    }
}