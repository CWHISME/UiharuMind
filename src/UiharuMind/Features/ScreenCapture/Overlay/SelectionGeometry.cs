using System;
using Avalonia;

namespace UiharuMind.Features.ScreenCapture.Overlay;

/// <summary>
/// 调整模式下选区的拖拽目标：<see cref="Move"/> 是整体平移，其余是缩放的边/角。
/// </summary>
public enum SelectionResizeHandle
{
    None,
    Move,
    TopLeft,
    Top,
    TopRight,
    Left,
    Right,
    BottomLeft,
    Bottom,
    BottomRight,
}

/// <summary>
/// 截图选框的纯几何：命中测试、平移钳制、边/角缩放钳制。
/// 不碰任何控件，App.Tests 直接单测（与 <see cref="CaptureDimBands"/> 同一口径）。
/// </summary>
public static class SelectionGeometry
{
    /// 边缘命中带宽度(DIP)：指针落在这条带内就算要拖边/角
    public const double HandleHitDistance = 6.0;

    /// 缩放时选框最小边长(DIP)，防拖没
    public const double MinSideLength = 2.0;

    /// <summary>
    /// 判断指针 <paramref name="p"/> 落在选区 <paramref name="selection"/> 的哪个拖拽目标上。
    /// 命中带宽度为 <see cref="HandleHitDistance"/>；角优先于边，选区内部为整体平移。
    /// </summary>
    /// <param name="selection">当前选区矩形</param>
    /// <param name="p">指针位置（窗口坐标）</param>
    /// <returns>命中的拖拽目标；不在任何命中范围内时为 <see cref="SelectionResizeHandle.None"/></returns>
    public static SelectionResizeHandle HitTest(Rect selection, Point p)
    {
        if (selection.Width <= 0 || selection.Height <= 0) return SelectionResizeHandle.None;

        bool nearLeft = Math.Abs(p.X - selection.Left) <= HandleHitDistance;
        bool nearRight = Math.Abs(p.X - selection.Right) <= HandleHitDistance;
        bool nearTop = Math.Abs(p.Y - selection.Top) <= HandleHitDistance;
        bool nearBottom = Math.Abs(p.Y - selection.Bottom) <= HandleHitDistance;

        // 角优先：拖角时指针同时贴近两条边，应认作角而不是某条边
        if (nearTop && nearLeft) return SelectionResizeHandle.TopLeft;
        if (nearTop && nearRight) return SelectionResizeHandle.TopRight;
        if (nearBottom && nearLeft) return SelectionResizeHandle.BottomLeft;
        if (nearBottom && nearRight) return SelectionResizeHandle.BottomRight;
        if (nearTop) return SelectionResizeHandle.Top;
        if (nearBottom) return SelectionResizeHandle.Bottom;
        if (nearLeft) return SelectionResizeHandle.Left;
        if (nearRight) return SelectionResizeHandle.Right;

        return selection.Contains(p) ? SelectionResizeHandle.Move : SelectionResizeHandle.None;
    }

    /// <summary>把选框平移到 <paramref name="requestedTopLeft"/>，夹在窗口内，尺寸不变</summary>
    /// <param name="selection">当前选区矩形</param>
    /// <param name="requestedTopLeft">希望的左上角位置（窗口坐标）</param>
    /// <param name="bounds">允许的平移边界（窗口大小）</param>
    /// <returns>平移后的选区；左上角被钳制在窗口内，尺寸不变</returns>
    public static Rect MoveTo(Rect selection, Point requestedTopLeft, Size bounds)
    {
        double width = Math.Max(0, selection.Width);
        double height = Math.Max(0, selection.Height);
        // 选框比窗口还大时（理论不发生）退化为贴边，不留负坐标
        double maxX = Math.Max(0, bounds.Width - width);
        double maxY = Math.Max(0, bounds.Height - height);
        double x = Math.Clamp(requestedTopLeft.X, 0, maxX);
        double y = Math.Clamp(requestedTopLeft.Y, 0, maxY);
        return new Rect(x, y, width, height);
    }

    /// <summary>
    /// 按边/角拖到 <paramref name="p"/>，对侧边/角锚定不动；
    /// 最小边长 <see cref="MinSideLength"/> 与窗口边界都会生效。
    /// </summary>
    /// <param name="selection">当前选区矩形</param>
    /// <param name="handle">拖拽的边/角；<see cref="SelectionResizeHandle.None"/> 或 <see cref="SelectionResizeHandle.Move"/> 时原样返回</param>
    /// <param name="p">指针位置（窗口坐标）</param>
    /// <param name="bounds">允许的缩放边界（窗口大小）</param>
    /// <returns>缩放后的选区；对侧锚定不动，最小边长与窗口边界生效</returns>
    public static Rect ResizeTo(Rect selection, SelectionResizeHandle handle, Point p, Size bounds)
    {
        if (handle == SelectionResizeHandle.None || handle == SelectionResizeHandle.Move)
            return selection;

        double left = selection.Left;
        double top = selection.Top;
        double right = selection.Right;
        double bottom = selection.Bottom;

        switch (handle)
        {
            case SelectionResizeHandle.TopLeft:
                left = ClampIfFeasible(p.X, 0, right - MinSideLength, left);
                top = ClampIfFeasible(p.Y, 0, bottom - MinSideLength, top);
                break;
            case SelectionResizeHandle.Top:
                top = ClampIfFeasible(p.Y, 0, bottom - MinSideLength, top);
                break;
            case SelectionResizeHandle.TopRight:
                top = ClampIfFeasible(p.Y, 0, bottom - MinSideLength, top);
                right = ClampIfFeasible(p.X, left + MinSideLength, bounds.Width, right);
                break;
            case SelectionResizeHandle.Left:
                left = ClampIfFeasible(p.X, 0, right - MinSideLength, left);
                break;
            case SelectionResizeHandle.Right:
                right = ClampIfFeasible(p.X, left + MinSideLength, bounds.Width, right);
                break;
            case SelectionResizeHandle.BottomLeft:
                left = ClampIfFeasible(p.X, 0, right - MinSideLength, left);
                bottom = ClampIfFeasible(p.Y, top + MinSideLength, bounds.Height, bottom);
                break;
            case SelectionResizeHandle.Bottom:
                bottom = ClampIfFeasible(p.Y, top + MinSideLength, bounds.Height, bottom);
                break;
            case SelectionResizeHandle.BottomRight:
                right = ClampIfFeasible(p.X, left + MinSideLength, bounds.Width, right);
                bottom = ClampIfFeasible(p.Y, top + MinSideLength, bounds.Height, bottom);
                break;
        }

        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>
    /// 把选区对齐到设备像素网格（<paramref name="renderScale"/> 倍缩放下）。
    /// 4 条暗带是独立矩形，Aliased 关掉反锯齿后，Skia 会把每条带子的非整数边界各自 round 到最近像素；
    /// 共享边界若恰在 .5 附近，整窗宽的顶带会多盖洞顶一行，出现横贯高亮区的 1px 暗线。
    /// 先对齐，让所有带子的共享边界落在同一整数设备像素上。
    /// </summary>
    /// <param name="selection">当前选区矩形</param>
    /// <param name="renderScale">设备缩放比（DIP 转设备像素的倍数）；非正或非有限值时原样返回</param>
    /// <returns>对齐设备像素网格后的选区</returns>
    public static Rect AlignToDevicePixels(Rect selection, double renderScale)
    {
        if (renderScale <= 0 || double.IsNaN(renderScale) || double.IsInfinity(renderScale)) return selection;

        double left = Math.Round(selection.Left * renderScale) / renderScale;
        double top = Math.Round(selection.Top * renderScale) / renderScale;
        double right = Math.Round(selection.Right * renderScale) / renderScale;
        double bottom = Math.Round(selection.Bottom * renderScale) / renderScale;
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// 范围合法就钳制；不合法（例如 1px 的框贴在最右边缘，右边没有伸展空间）就保持原值，绝不抛异常
    private static double ClampIfFeasible(double value, double min, double max, double fallback)
    {
        return min <= max ? Math.Clamp(value, min, max) : fallback;
    }
}