using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using UiharuMind.Features.ScreenCapture;
using UiharuMind.Features.ScreenCapture.Drawing;

namespace UiharuMind.App.Tests.ScreenCapture;

/// <summary>
/// 标注命令的几何正确性：命令存的是原始像素坐标系数据，
/// <see cref="IDrawCommand.CreateControl"/> 直接按数据摆控件——这条链错了，
/// 「放大后画的标注保存时错位」就会回归。这里把每种命令的几何钉死。
/// </summary>
public class DrawCommandGeometryTests
{
    private static readonly Color TestColor = Colors.Red;

    [Fact]
    public void RectangleCommand_对角拖拽_几何正确()
    {
        var command = new RectangleCommand(new Point(10, 10), new Point(110, 60), TestColor, 3);
        var rectangle = Assert.IsType<Rectangle>(command.CreateControl());

        Assert.Equal(10, Canvas.GetLeft(rectangle));
        Assert.Equal(10, Canvas.GetTop(rectangle));
        Assert.Equal(100, rectangle.Width);
        Assert.Equal(50, rectangle.Height);
        Assert.Equal(3, rectangle.StrokeThickness);
    }

    [Fact]
    public void RectangleCommand_反向拖拽_取最小值为左上()
    {
        var command = new RectangleCommand(new Point(110, 60), new Point(10, 10), TestColor, 3);
        var rectangle = Assert.IsType<Rectangle>(command.CreateControl());

        Assert.Equal(10, Canvas.GetLeft(rectangle));
        Assert.Equal(10, Canvas.GetTop(rectangle));
        Assert.Equal(100, rectangle.Width);
        Assert.Equal(50, rectangle.Height);
    }

    [Fact]
    public void EllipseCommand_对角拖拽_以外接矩形内切椭圆()
    {
        var command = new EllipseCommand(new Point(10, 10), new Point(110, 60), TestColor, 3);
        var ellipse = Assert.IsType<Ellipse>(command.CreateControl());

        Assert.Equal(10, Canvas.GetLeft(ellipse));
        Assert.Equal(10, Canvas.GetTop(ellipse));
        Assert.Equal(100, ellipse.Width);
        Assert.Equal(50, ellipse.Height);
    }

    [Fact]
    public void LineCommand_起点固定_拖动只改终点()
    {
        var command = new LineCommand(new Point(5, 5), new Point(50, 50), TestColor, 3);
        var line = Assert.IsType<Line>(command.CreateControl());
        Assert.Equal(new Point(5, 5), line.StartPoint);
        Assert.Equal(new Point(50, 50), line.EndPoint);

        command.UpdateControl(line, new Point(80, 20));
        Assert.Equal(new Point(5, 5), line.StartPoint);
        Assert.Equal(new Point(80, 20), line.EndPoint);
    }

    [Fact]
    public void ArrowLineCommand_线宽透传给箭头控件()
    {
        var command = new ArrowLineCommand(new Point(0, 0), new Point(100, 100), TestColor, 4);
        var arrow = Assert.IsType<ArrowLineControl>(command.CreateControl());
        Assert.Equal(4, arrow.StrokeThickness);
    }

    [Fact]
    public void PenCommand_点序列累积成折线()
    {
        var command = new PenCommand(new Point(0, 0), TestColor, 3);
        command.AddPoint(new Point(10, 10));
        command.AddPoint(new Point(20, 5));

        var polyline = Assert.IsType<Polyline>(command.CreateControl());
        Assert.Equal(3, polyline.Points.Count);
        Assert.Equal(new Point(10, 10), polyline.Points[1]);
        Assert.Equal(new Point(20, 5), polyline.Points[2]);
        // Fill 必须为 null：Polyline 只要 Fill 非空就按闭合填充路径生成，首尾会被直线连起来
        Assert.Null(polyline.Fill);
    }

    [Fact]
    public void TextCommand_落点与内容原样()
    {
        var command = new TextCommand(new Point(40, 30), TestColor, 16, "你好");
        var textBlock = Assert.IsType<TextBlock>(command.CreateControl());

        Assert.Equal("你好", textBlock.Text);
        Assert.Equal(16, textBlock.FontSize);
        Assert.Equal(40, Canvas.GetLeft(textBlock));
        Assert.Equal(30, Canvas.GetTop(textBlock));
    }
}
