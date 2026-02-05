using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace UiharuMind.Views.Windows.ScreenCapture;

public class ArrowLineControl : Control
{
    private readonly Point _startPoint;
    private Point _endPoint;
    private readonly Color _color;

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
        var pen = new Pen(new SolidColorBrush(_color), 2);
        context.DrawLine(pen, _startPoint, _endPoint);

        // 绘制箭头
        DrawArrow(context, _startPoint, _endPoint, pen);
    }

    private void DrawArrow(DrawingContext context, Point start, Point end, Pen pen)
    {
        const double arrowLength = 10;
        const double arrowAngle = Math.PI / 6;

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