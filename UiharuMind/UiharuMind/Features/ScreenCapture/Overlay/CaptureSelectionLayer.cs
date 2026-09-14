using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;

namespace UiharuMind.Features.ScreenCapture.Overlay;

/// <summary>
/// 选区层：一个跟手的选框，加围在它四周、把选区亮出来的暗色带子。对外只认窗内 DIP 坐标，
/// 窗口事件与全局钩子补位都走同一套入口（同一次物理事件重复调用是幂等的）。
/// </summary>
internal sealed class CaptureSelectionLayer
{
    private readonly Rectangle _rectangle;
    private readonly Rectangle _dimTop;
    private readonly Rectangle _dimBottom;
    private readonly Rectangle _dimLeft;
    private readonly Rectangle _dimRight;
    private readonly Func<Size> _windowSize;
    private Point _startPoint;

    public CaptureSelectionLayer(Rectangle rectangle, Rectangle dimTop, Rectangle dimBottom,
        Rectangle dimLeft, Rectangle dimRight, Func<Size> windowSize)
    {
        _rectangle = rectangle;
        _dimTop = dimTop;
        _dimBottom = dimBottom;
        _dimLeft = dimLeft;
        _dimRight = dimRight;
        _windowSize = windowSize;
    }

    /// <summary>是否正在框选</summary>
    public bool IsSelecting { get; private set; }

    /// <summary>本次框选的起点（窗内 DIP）</summary>
    public Point StartPoint => _startPoint;

    /// <summary>当前选区（窗内 DIP）；未框选时为空矩形</summary>
    public Rect Selection => new(Canvas.GetLeft(_rectangle), Canvas.GetTop(_rectangle),
        _rectangle.Width, _rectangle.Height);

    public void Begin(Point windowDip)
    {
        IsSelecting = true;
        _startPoint = windowDip;
        Place(_rectangle, windowDip.X, windowDip.Y, 0, 0);
    }

    public void Update(Point windowDip)
    {
        Place(_rectangle,
            Math.Ceiling(Math.Min(_startPoint.X, windowDip.X)),
            Math.Ceiling(Math.Min(_startPoint.Y, windowDip.Y)),
            Math.Ceiling(Math.Abs(windowDip.X - _startPoint.X)),
            Math.Ceiling(Math.Abs(windowDip.Y - _startPoint.Y)));
        RefreshDim();
    }

    /// <summary>结束框选，选区保留供调用方裁剪</summary>
    public void Finish()
    {
        IsSelecting = false;
    }

    public void Reset()
    {
        IsSelecting = false;
        _startPoint = default;
        Place(_rectangle, 0, 0, 0, 0);
        RefreshDim();
    }

    /// <summary>
    /// 刷新暗层：框选中留出选区那块洞，没有则全暗。
    /// </summary>
    public void RefreshDim()
    {
        var selection = Selection;
        bool hasHole = IsSelecting && selection.Width > 0 && selection.Height > 0;
        var bands = CaptureDimBands.Calculate(_windowSize(), hasHole ? selection : default);

        Place(_dimTop, bands.Top);
        Place(_dimBottom, bands.Bottom);
        Place(_dimLeft, bands.Left);
        Place(_dimRight, bands.Right);
    }

    private static void Place(Rectangle target, Rect rect)
    {
        Place(target, rect.X, rect.Y, rect.Width, rect.Height);
    }

    private static void Place(Rectangle target, double x, double y, double width, double height)
    {
        Canvas.SetLeft(target, x);
        Canvas.SetTop(target, y);
        target.Width = Math.Max(0, width);
        target.Height = Math.Max(0, height);
    }
}
