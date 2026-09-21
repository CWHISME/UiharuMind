using System;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;

namespace UiharuMind.Features.ScreenCapture.Overlay;

/// <summary>
/// 选区层：一个跟手的选框，加围在它四周、把选区亮出来的暗色带子，以及调整模式下四个角的柄。
/// 对外只认窗内 DIP 坐标，窗口事件与全局钩子补位都走同一套入口（同一次物理事件重复调用是幂等的）。
/// 调整模式的纯几何收在 <see cref="SelectionGeometry"/>，本类只管把结果落到控件上。
/// </summary>
internal sealed class CaptureSelectionLayer
{
    /// 角柄边长(DIP)，与 axaml 里四个柄的 Width/Height 保持一致
    private const double HandleSize = 9.0;

    private readonly Rectangle _rectangle;
    private readonly Rectangle _dimTop;
    private readonly Rectangle _dimBottom;
    private readonly Rectangle _dimLeft;
    private readonly Rectangle _dimRight;
    private readonly Rectangle _handleTopLeft;
    private readonly Rectangle _handleTopRight;
    private readonly Rectangle _handleBottomLeft;
    private readonly Rectangle _handleBottomRight;
    private readonly Func<Size> _windowSize;
    private readonly Func<double> _renderScaling;
    private Point _startPoint;

    /// <summary>是否正在框选</summary>
    public bool IsSelecting { get; private set; }

    /// <summary>是否处于调整模式：框选已结束，选框保留下来可拖动/缩放</summary>
    public bool IsAdjusting { get; private set; }

    /// <summary>框选途中按住了修饰键：虚线+角柄预告「松手进入调整模式」是否亮着</summary>
    public bool AdjustPreviewArmed { get; private set; }

    /// <summary>本次框选的起点（窗内 DIP）</summary>
    public Point StartPoint => _startPoint;

    /// <summary>当前选区（窗内 DIP）；未框选时为空矩形</summary>
    public Rect Selection => new(Canvas.GetLeft(_rectangle), Canvas.GetTop(_rectangle),
        _rectangle.Width, _rectangle.Height);

    public CaptureSelectionLayer(Rectangle rectangle, Rectangle dimTop, Rectangle dimBottom,
        Rectangle dimLeft, Rectangle dimRight,
        Rectangle handleTopLeft, Rectangle handleTopRight, Rectangle handleBottomLeft,
        Rectangle handleBottomRight, Func<Size> windowSize, Func<double> renderScaling)
    {
        _rectangle = rectangle;
        _dimTop = dimTop;
        _dimBottom = dimBottom;
        _dimLeft = dimLeft;
        _dimRight = dimRight;
        _handleTopLeft = handleTopLeft;
        _handleTopRight = handleTopRight;
        _handleBottomLeft = handleBottomLeft;
        _handleBottomRight = handleBottomRight;
        _windowSize = windowSize;
        _renderScaling = renderScaling;
    }

    public void Begin(Point windowDip)
    {
        IsSelecting = true;
        IsAdjusting = false;
        AdjustPreviewArmed = false;
        SetDashed(false);
        _startPoint = windowDip;
        Place(_rectangle, windowDip.X, windowDip.Y, 0, 0);
        PlaceHandles();
    }

    public void Update(Point windowDip)
    {
        Place(_rectangle,
            Math.Ceiling(Math.Min(_startPoint.X, windowDip.X)),
            Math.Ceiling(Math.Min(_startPoint.Y, windowDip.Y)),
            Math.Ceiling(Math.Abs(windowDip.X - _startPoint.X)),
            Math.Ceiling(Math.Abs(windowDip.Y - _startPoint.Y)));
        PlaceHandles();
        RefreshDim();
    }

    /// <summary>结束框选，选区保留供调用方裁剪</summary>
    public void Finish()
    {
        IsSelecting = false;
    }

    /// <summary>结束框选并进入调整模式：选框与暗洞保留，改虚线、亮角柄</summary>
    public void EnterAdjust()
    {
        IsSelecting = false;
        IsAdjusting = true;
        AdjustPreviewArmed = false;
        SetDashed(true);
        PlaceHandles();
        RefreshDim();
    }

    public void Reset()
    {
        IsSelecting = false;
        IsAdjusting = false;
        AdjustPreviewArmed = false;
        SetDashed(false);
        _startPoint = default;
        Place(_rectangle, 0, 0, 0, 0);
        PlaceHandles();
        RefreshDim();
    }

    /// <summary>命中测试：落在哪条边/哪个角上（<see cref="SelectionResizeHandle.Move"/> 表示框内）</summary>
    public SelectionResizeHandle HitTest(Point p)
    {
        return SelectionGeometry.HitTest(Selection, p);
    }

    /// <summary>框选途中按修饰键时的预告态：亮虚线与角柄；松开则熄灭。只在框选中响应</summary>
    public void ShowAdjustPreview(bool armed)
    {
        if (!IsSelecting || AdjustPreviewArmed == armed) return;
        AdjustPreviewArmed = armed;
        SetDashed(armed);
        PlaceHandles();
    }

    /// <summary>平移整个选框（增量），夹在窗口内</summary>
    public void MoveBy(Point delta, Size windowSize)
    {
        var r = Selection;
        var next = SelectionGeometry.MoveTo(r, new Point(r.X + delta.X, r.Y + delta.Y), windowSize);
        Place(_rectangle, next.X, next.Y, next.Width, next.Height);
        RefreshDim();
        PlaceHandles();
    }

    /// <summary>按边/角缩放选框，夹最小尺寸与窗口边界</summary>
    public void ResizeTo(SelectionResizeHandle handle, Point pointer, Size windowSize)
    {
        var next = SelectionGeometry.ResizeTo(Selection, handle, pointer, windowSize);
        Place(_rectangle, next.X, next.Y, next.Width, next.Height);
        RefreshDim();
        PlaceHandles();
    }

    /// <summary>
    /// 刷新暗层：框选或调整态留出选区那块洞，否则全暗。
    /// </summary>
    public void RefreshDim()
    {
        var selection = Selection;
        bool hasHole = (IsSelecting || IsAdjusting) && selection.Width > 0 && selection.Height > 0;
        // 洞边界先对齐设备像素网格：4 条带子是独立矩形，Aliased 下 Skia 会把每条带子的非整数边界
        // 各自 round，共享边界落在 .5 附近时，整窗宽的顶带会多盖洞顶一行（横贯高亮区的 1px 暗线）
        var hole = hasHole ? SelectionGeometry.AlignToDevicePixels(selection, _renderScaling()) : default;
        var bands = CaptureDimBands.Calculate(_windowSize(), hole);

        Place(_dimTop, bands.Top);
        Place(_dimBottom, bands.Bottom);
        Place(_dimLeft, bands.Left);
        Place(_dimRight, bands.Right);
    }

    private void SetDashed(bool on)
    {
        if (on)
        {
            _rectangle.StrokeDashArray ??= new AvaloniaList<double>();
            _rectangle.StrokeDashArray.Clear();
            _rectangle.StrokeDashArray.Add(4);
            _rectangle.StrokeDashArray.Add(3);
        }
        else
        {
            _rectangle.StrokeDashArray?.Clear();
        }
    }

    private void PlaceHandles()
    {
        bool show = IsAdjusting || AdjustPreviewArmed;
        _handleTopLeft.IsVisible = show;
        _handleTopRight.IsVisible = show;
        _handleBottomLeft.IsVisible = show;
        _handleBottomRight.IsVisible = show;
        if (!show) return;

        var r = Selection;
        PlaceHandle(_handleTopLeft, r.TopLeft);
        PlaceHandle(_handleTopRight, r.TopRight);
        PlaceHandle(_handleBottomLeft, r.BottomLeft);
        PlaceHandle(_handleBottomRight, r.BottomRight);
    }

    private static void PlaceHandle(Rectangle handle, Point corner)
    {
        Canvas.SetLeft(handle, corner.X - HandleSize / 2);
        Canvas.SetTop(handle, corner.Y - HandleSize / 2);
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