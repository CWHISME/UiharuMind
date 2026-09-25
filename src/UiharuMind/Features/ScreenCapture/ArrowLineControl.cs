using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace UiharuMind.Features.ScreenCapture;

public class ArrowLineControl : Control
{
    private readonly Point _startPoint;
    private Point _endPoint;
    private readonly Color _color;

    /// <summary>线宽（原始像素单位），默认 2</summary>
    public double StrokeThickness { get; set; } = 2;

    public ArrowLineControl(Point startPoint, Point endPoint, Color color)
    {
        _startPoint = startPoint;
        _endPoint = endPoint;
        _color = color;
    }

    public void UpdateEndPoint(Point endPoint)
    {
        _endPoint = endPoint;
        InvalidateVisual();
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
}