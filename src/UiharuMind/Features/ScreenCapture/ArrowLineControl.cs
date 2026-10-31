using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering;

namespace UiharuMind.Features.ScreenCapture;

/// <summary>
/// 箭头线控件：只认<b>自身局部坐标</b>（控件左上角为原点），位置与尺寸由
/// <see cref="Drawing.ArrowLineCommand"/> 按包围盒摆好（Canvas.Left/Top + Width/Height），
/// 与矩形/椭圆命令同一套分工——命令持有几何，控件只管画。
/// <para>
/// 必须让 Bounds 与画出来的内容一致：Bounds 为 0×0 的控件在屏幕上照画不误，
/// 但 <c>RenderTargetBitmap.Render</c> 走的 ImmediateRenderer 会拿 Bounds 与裁剪矩形求交，
/// 空矩形永不相交，整条箭头被跳过（「贴图编辑里箭头看得见，双击保存后没了」）。
/// </para>
/// </summary>
public class ArrowLineControl : Control, ICustomHitTest
{
    private const double HitPadding = 6; //点击容差（原始像素），线太细时也点得中

    private Point _startPoint; //局部坐标
    private Point _endPoint; //局部坐标
    private readonly Color _color;

    /// <summary>线宽（原始像素单位）</summary>
    public double StrokeThickness { get; }

    public ArrowLineControl(double strokeThickness, Color color)
    {
        StrokeThickness = strokeThickness;
        _color = color;
    }

    /// <summary>设置两端点（<b>局部坐标</b>，相对控件左上角）</summary>
    public void SetGeometry(Point startPoint, Point endPoint)
    {
        _startPoint = startPoint;
        _endPoint = endPoint;
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>
    /// 只命中线附近：包围盒是一整块矩形，按 Bounds 命中会让斜线旁边的空白也吃掉点击。
    /// </summary>
    public bool HitTest(Point point) => DistanceToSegment(point) <= StrokeThickness / 2 + HitPadding;

    /// <summary>
    /// 兜底尺寸：正常路径下 <see cref="Drawing.ArrowLineCommand"/> 会显式设 Width/Height，
    /// 这里只保证「裸 new 出来直接挂画布」时也不会退化成 0×0 而被裁掉。
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(
            Math.Max(_startPoint.X, _endPoint.X) + StrokeThickness,
            Math.Max(_startPoint.Y, _endPoint.Y) + StrokeThickness);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // 绘制主线条
        var pen = new Pen(new SolidColorBrush(_color), StrokeThickness);
        context.DrawLine(pen, _startPoint, _endPoint);

        // 绘制箭头
        DrawArrow(context, _startPoint, _endPoint, pen);
    }

    private void DrawArrow(DrawingContext context, Point start, Point end, Pen pen)
    {
        const double arrowAngle = Math.PI / 6;

        // 箭头长度跟线宽联动：线宽变细时箭头不至于突兀（原写死 10 是按线宽 2 配的）
        double arrowLength = Math.Max(6, StrokeThickness * 5);

        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var angle = Math.Atan2(dy, dx);

        var x1 = end.X - arrowLength * Math.Cos(angle - arrowAngle);
        var y1 = end.Y - arrowLength * Math.Sin(angle - arrowAngle);
        var x2 = end.X - arrowLength * Math.Cos(angle + arrowAngle);
        var y2 = end.Y - arrowLength * Math.Sin(angle + arrowAngle);

        context.DrawLine(pen, end, new Point(x1, y1));
        context.DrawLine(pen, end, new Point(x2, y2));
    }

    private double DistanceToSegment(Point point)
    {
        double dx = _endPoint.X - _startPoint.X;
        double dy = _endPoint.Y - _startPoint.Y;
        double lengthSquared = dx * dx + dy * dy;
        double px = point.X - _startPoint.X;
        double py = point.Y - _startPoint.Y;

        if (lengthSquared < 1e-6) return Math.Sqrt(px * px + py * py);

        double t = Math.Clamp((px * dx + py * dy) / lengthSquared, 0, 1);
        double ex = px - t * dx;
        double ey = py - t * dy;
        return Math.Sqrt(ex * ex + ey * ey);
    }
}
