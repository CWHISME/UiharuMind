using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace UiharuMind.Features.ScreenCapture.Drawing;

/// <summary>
/// 一条标注命令：纯几何数据 + 生成控件。
/// 可多次 <see cref="CreateControl"/>——显示画布一份、导出画布一份，互不干扰；
/// 撤销/重做由编辑器维护「命令 + 控件」配对，redo 恢复的是控件引用而非重放。
/// </summary>
public interface IDrawCommand
{
    /// <summary>按当前数据生成一个新控件（不挂到任何画布）</summary>
    Control CreateControl();

    /// <summary>拖动中更新终点并同步控件外观</summary>
    void UpdateControl(Control control, Point end);
}

/// <summary>矩形标注（对角拖拽）</summary>
public sealed class RectangleCommand : IDrawCommand
{
    public Point Start { get; }
    public Point End { get; private set; }
    public Color Color { get; }
    public double StrokeThickness { get; }

    public RectangleCommand(Point start, Point end, Color color, double strokeThickness)
    {
        Start = start;
        End = end;
        Color = color;
        StrokeThickness = strokeThickness;
    }

    public Control CreateControl()
    {
        var rectangle = new Rectangle
        {
            Stroke = new SolidColorBrush(Color),
            StrokeThickness = StrokeThickness,
            Fill = Brushes.Transparent
        };
        ApplyTo(rectangle);
        return rectangle;
    }

    public void UpdateControl(Control control, Point end)
    {
        End = end;
        if (control is Rectangle rectangle) ApplyTo(rectangle);
    }

    private void ApplyTo(Rectangle rectangle)
    {
        Canvas.SetLeft(rectangle, Math.Min(Start.X, End.X));
        Canvas.SetTop(rectangle, Math.Min(Start.Y, End.Y));
        rectangle.Width = Math.Abs(End.X - Start.X);
        rectangle.Height = Math.Abs(End.Y - Start.Y);
    }
}

/// <summary>椭圆标注（对角拖拽，宽高可不同；对应工具按钮 Circle）</summary>
public sealed class EllipseCommand : IDrawCommand
{
    public Point Start { get; }
    public Point End { get; private set; }
    public Color Color { get; }
    public double StrokeThickness { get; }

    public EllipseCommand(Point start, Point end, Color color, double strokeThickness)
    {
        Start = start;
        End = end;
        Color = color;
        StrokeThickness = strokeThickness;
    }

    public Control CreateControl()
    {
        var ellipse = new Ellipse
        {
            Stroke = new SolidColorBrush(Color),
            StrokeThickness = StrokeThickness,
            Fill = Brushes.Transparent
        };
        ApplyTo(ellipse);
        return ellipse;
    }

    public void UpdateControl(Control control, Point end)
    {
        End = end;
        if (control is Ellipse ellipse) ApplyTo(ellipse);
    }

    private void ApplyTo(Ellipse ellipse)
    {
        var centerX = (Start.X + End.X) / 2;
        var centerY = (Start.Y + End.Y) / 2;
        var radiusX = Math.Abs(End.X - Start.X) / 2;
        var radiusY = Math.Abs(End.Y - Start.Y) / 2;

        Canvas.SetLeft(ellipse, centerX - radiusX);
        Canvas.SetTop(ellipse, centerY - radiusY);
        ellipse.Width = radiusX * 2;
        ellipse.Height = radiusY * 2;
    }
}

/// <summary>直线</summary>
public sealed class LineCommand : IDrawCommand
{
    public Point Start { get; }
    public Point End { get; private set; }
    public Color Color { get; }
    public double StrokeThickness { get; }

    public LineCommand(Point start, Point end, Color color, double strokeThickness)
    {
        Start = start;
        End = end;
        Color = color;
        StrokeThickness = strokeThickness;
    }

    public Control CreateControl()
    {
        var line = new Line
        {
            Stroke = new SolidColorBrush(Color),
            StrokeThickness = StrokeThickness,
            StartPoint = Start,
            EndPoint = End
        };
        return line;
    }

    public void UpdateControl(Control control, Point end)
    {
        End = end;
        if (control is Line line) line.EndPoint = End;
    }
}

/// <summary>箭头线（线宽透传给 <see cref="ArrowLineControl"/>）</summary>
public sealed class ArrowLineCommand : IDrawCommand
{
    public Point Start { get; }
    public Point End { get; private set; }
    public Color Color { get; }
    public double StrokeThickness { get; }

    public ArrowLineCommand(Point start, Point end, Color color, double strokeThickness)
    {
        Start = start;
        End = end;
        Color = color;
        StrokeThickness = strokeThickness;
    }

    public Control CreateControl()
    {
        return new ArrowLineControl(Start, End, Color)
        {
            StrokeThickness = StrokeThickness
        };
    }

    public void UpdateControl(Control control, Point end)
    {
        End = end;
        if (control is ArrowLineControl arrow) arrow.UpdateEndPoint(end);
    }
}

/// <summary>
/// 自由画笔（随手画：点序列折线）。导出/撤销语义与其它命令一致。
/// </summary>
public sealed class PenCommand : IDrawCommand
{
    public Color Color { get; }
    public double StrokeThickness { get; }
    public IReadOnlyList<Point> Points => _points;

    private readonly List<Point> _points = new();

    public PenCommand(Point start, Color color, double strokeThickness)
    {
        _points.Add(start);
        Color = color;
        StrokeThickness = strokeThickness;
    }

    public void AddPoint(Point point) => _points.Add(point);

    public Control CreateControl()
    {
        var polyline = new Polyline
        {
            Points = new Points(_points),
            Stroke = new SolidColorBrush(Color),
            StrokeThickness = StrokeThickness,
            StrokeLineCap = PenLineCap.Round
            // 注意：不能设 Fill——Polyline 只要 Fill 非空就按闭合填充路径生成，
            // 描边会把首尾连起来（用户反馈的「自由笔刷起止点闭合直线」）
        };
        return polyline;
    }

    public void UpdateControl(Control control, Point end)
    {
        if (control is Polyline polyline) polyline.Points = new Points(_points);
    }
}

/// <summary>
/// 文字标注。<see cref="Position"/> 是图上最终落点（输入框换算后的 TextBlock 位置）。
/// </summary>
public sealed class TextCommand : IDrawCommand
{
    public Point Position { get; }
    public string Text { get; }
    public Color Color { get; }
    public double FontSize { get; }

    public TextCommand(Point position, Color color, double fontSize, string text)
    {
        Position = position;
        Color = color;
        FontSize = fontSize;
        Text = text;
    }

    public Control CreateControl()
    {
        var textBlock = new TextBlock
        {
            Text = Text,
            Foreground = new SolidColorBrush(Color),
            FontSize = FontSize,
            FontWeight = FontWeight.Bold
        };
        Canvas.SetLeft(textBlock, Position.X);
        Canvas.SetTop(textBlock, Position.Y);
        return textBlock;
    }

    public void UpdateControl(Control control, Point end)
    {
    }
}
