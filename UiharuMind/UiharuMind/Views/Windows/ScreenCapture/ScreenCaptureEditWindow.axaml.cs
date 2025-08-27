using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpHook.Native;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Input;
using UiharuMind.Utils;

namespace UiharuMind.Views.Windows.ScreenCapture;

public partial class ScreenCaptureEditWindow : Window
{
    private Bitmap? _source;
    private Action<Bitmap> _onClose;

    private Stack<Path> _undoStack = new Stack<Path>();
    private Stack<Path> _redoStack = new Stack<Path>();

    public int BtnHeight { get; set; } = 45;

    private Size _size;
    private double _scale;

    private static Color _color = Colors.Red;

    // private KeyCombinationData _undoKey;
    // private KeyCombinationData _redoKey;

    public ScreenCaptureEditWindow(Bitmap source, PixelPoint position, Size size, Action<Bitmap> onClose)
    {
        _source = source;
        _onClose = onClose;
        _size = size;
        _scale = size.Width / source.PixelSize.Width;
        int offset = (int)(App.ScreensService.Scaling * 5);
        var pos = new PixelPoint(position.X - offset, position.Y - offset);
        Width = size.Width + 10;
        Height = size.Height + BtnHeight + 10;
        MaxHeight = Height;
        MinHeight = Height;
        MaxWidth = Width;
        MinWidth = Width;

        CanResize = false;
        this.SetSimpledecorationPureWindow();
        DataContext = this;
        InitializeComponent();

        Position = pos; //position;

        GeometryRectangleButton.IsCheckedChanged += OnGeometryRectangleButtonIsCheckedChanged;
        GeometryCircleButton.IsCheckedChanged += OnGeometryCircleButtonIsCheckedChanged;
        GeometryRectangleButton.IsChecked = true;

        DrawingCanvas.PointerPressed += OnDrawingCanvasPointerPressed;
        DrawingCanvas.PointerMoved += OnDrawingCanvasPointerMoved;
        DrawingCanvas.PointerReleased += OnDrawingCanvasPointerReleased;

        // _undoKey = new KeyCombinationData(KeyCode.VcZ, Undo, [KeyCode.VcLeftControl],
        //     "ScreenCaptureEditWindow Undo");
        // _redoKey = new KeyCombinationData(KeyCode.VcY, Redo, [KeyCode.VcLeftControl],
        //     "ScreenCaptureEditWindow Redo");
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ImageContent.Source = _source;
        ImageContent.Width = _size.Width;
        ImageContent.Height = _size.Height;
        DrawingCanvas.Width = _size.Width;
        DrawingCanvas.Height = _size.Height;

        GeometryColorPicker.Color = _color;
        RefreshUndoRedo();

        Focusable = true;
        Focus();
    }

    // protected override void OnLoaded(RoutedEventArgs e)
    // {
    //     base.OnLoaded(e);
    //     InputManager.Instance.RegisterKey(_undoKey);
    //     InputManager.Instance.RegisterKey(_redoKey);
    // }
    //
    // protected override void OnUnloaded(RoutedEventArgs e)
    // {
    //     base.OnUnloaded(e);
    //     InputManager.Instance.UnRegisterKey(_undoKey);
    //     InputManager.Instance.UnRegisterKey(_redoKey);
    // }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        PointerUpdateKind pointerUpdateKind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        if (pointerUpdateKind == PointerUpdateKind.LeftButtonPressed && e.ClickCount >= 2)
        {
            SafeClose();
        }
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        SafeClose();
    }

    // private void SaveButton_Click(object? sender, RoutedEventArgs e)
    // {
    //     SafeClose();
    // }

    private void OnGeometryRectangleButtonIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (GeometryRectangleButton.IsChecked == true)
        {
            GeometryCircleButton.IsChecked = false;
        }
        //如果旁边未选中，那么不允许自己不选中
        else if (GeometryCircleButton.IsChecked == false)
        {
            GeometryRectangleButton.IsChecked = true;
        }
    }

    private void OnGeometryCircleButtonIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (GeometryCircleButton.IsChecked == true)
        {
            GeometryRectangleButton.IsChecked = false;
        }
        //如果旁边未选中，那么不允许自己不选中
        else if (GeometryRectangleButton.IsChecked == false)
        {
            GeometryCircleButton.IsChecked = true;
        }
    }

    [RelayCommand]
    public void Undo()
    {
        if (_undoStack.Count > 0)
        {
            var o = _undoStack.Pop();
            _redoStack.Push(o);
            DrawingCanvas.Children.Remove(o);
        }

        RefreshUndoRedo();
    }

    [RelayCommand]
    public void Redo()
    {
        if (_redoStack.Count > 0)
        {
            var o = _redoStack.Pop();
            _undoStack.Push(o);
            DrawingCanvas.Children.Add(o);
        }

        RefreshUndoRedo();
    }

    private void RefreshUndoRedo()
    {
        UndoButton.IsEnabled = _undoStack.Count > 0;
        RedoButton.IsEnabled = _redoStack.Count > 0;
    }

    //绘制==============

    private bool _isDrawing;
    private Point _startPoint;
    private Geometry? _currentGeometry;

    private void OnDrawingCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(DrawingCanvas).Properties.IsLeftButtonPressed)
        {
            _redoStack.Clear();

            _isDrawing = true;
            _startPoint = e.GetPosition(DrawingCanvas);
            _currentGeometry = GeometryRectangleButton.IsChecked == true
                ? new RectangleGeometry(new Rect(_startPoint, new Size(0, 0)))
                : new EllipseGeometry();

            _color = GeometryColorPicker.Color;
            var path = new Path
            {
                Data = _currentGeometry,
                Fill = Brushes.Transparent,
                StrokeThickness = 2,
                Stroke = new SolidColorBrush(_color)
            };
            DrawingCanvas.Children.Add(path);
            _undoStack.Push(path);
            RefreshUndoRedo();
        }
    }

    private void OnDrawingCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDrawing && DrawingCanvas != null)
        {
            const int borderX = 2;
            const int borderY = 1;
            var canvasBounds = new Rect(DrawingCanvas.Bounds.X + borderX, DrawingCanvas.Bounds.Y + borderY,
                DrawingCanvas.Bounds.Width - borderX,
                DrawingCanvas.Bounds.Height - borderX);
            var position = e.GetPosition(DrawingCanvas);

            position = new Point(
                Math.Max(Math.Min(position.X, canvasBounds.Right), canvasBounds.Left),
                Math.Max(Math.Min(position.Y, canvasBounds.Bottom), canvasBounds.Top));

            switch (_currentGeometry)
            {
                case RectangleGeometry rect:
                    var startPoint = _startPoint;

                    double left = Math.Min(startPoint.X, position.X);
                    double top = Math.Min(startPoint.Y, position.Y);
                    double right = Math.Max(startPoint.X, position.X);
                    double bottom = Math.Max(startPoint.Y, position.Y);

                    left = Math.Max(left, canvasBounds.Left);
                    top = Math.Max(top, canvasBounds.Top);
                    right = Math.Min(right, canvasBounds.Right);
                    bottom = Math.Min(bottom, canvasBounds.Bottom);

                    rect.Rect = new Rect(left, top, right - left, bottom - top);
                    break;
                case EllipseGeometry ellipse:
                    var center = _startPoint;
                    double radiusX = Math.Abs(position.X - center.X);
                    double radiusY = Math.Abs(position.Y - center.Y);

                    if (position.X < center.X)
                    {
                        center = new Point(center.X - Math.Min(radiusX, center.X - canvasBounds.Left), center.Y);
                        radiusX = Math.Min(radiusX, center.X - canvasBounds.Left);
                    }
                    else
                    {
                        center = new Point(center.X + Math.Min(radiusX, canvasBounds.Right - center.X), center.Y);
                        radiusX = Math.Min(radiusX, canvasBounds.Right - center.X);
                    }

                    if (position.Y < center.Y)
                    {
                        center = new Point(center.X, center.Y - Math.Min(radiusY, center.Y - canvasBounds.Top));
                        radiusY = Math.Min(radiusY, center.Y - canvasBounds.Top);
                    }
                    else
                    {
                        center = new Point(center.X, center.Y + Math.Min(radiusY, canvasBounds.Bottom - center.Y));
                        radiusY = Math.Min(radiusY, canvasBounds.Bottom - center.Y);
                    }

                    ellipse.Center = center;
                    ellipse.RadiusX = radiusX;
                    ellipse.RadiusY = radiusY;
                    // Log.Debug($"center:{center}, radiusX:{radiusX}, radiusY:{radiusY}");
                    break;
            }
        }
    }

    private void OnDrawingCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDrawing)
        {
            _isDrawing = false;
            // CombineCanvasWithBitmap(DrawingCanvas, _source);
        }
    }

    public Bitmap RenderToBitmap(Layoutable visual, Bitmap originalBitmap)
    {
        var renderTargetBitmap = new RenderTargetBitmap(originalBitmap.PixelSize);
        ImageContent.Width = originalBitmap.PixelSize.Width;
        ImageContent.Height = originalBitmap.PixelSize.Height;
        DrawingCanvas.Width = originalBitmap.PixelSize.Width;
        DrawingCanvas.Height = originalBitmap.PixelSize.Height;
        ResizeCanvasAndChildren(DrawingCanvas, originalBitmap.PixelSize.Width, originalBitmap.PixelSize.Height);
        visual.Arrange(new Rect(0, 0, originalBitmap.PixelSize.Width, originalBitmap.PixelSize.Height));
        renderTargetBitmap.Render(visual);
        return renderTargetBitmap;
    }

    private void ResizeCanvasAndChildren(Canvas canvas, double originalWidth, double originalHeight)
    {
        // 计算 Canvas 经过缩放后的的大小，保持与原图一致
        double scaleX = originalWidth / canvas.Bounds.Width;
        double scaleY = originalHeight / canvas.Bounds.Height;
        // canvas.Width = originalWidth;
        // canvas.Height = originalHeight;

        //同时遍历设置 Canvas 的所有子对象
        foreach (var child in canvas.Children)
        {
            if (child is Path path)
            {
                switch (path.Data)
                {
                    case RectangleGeometry rect:
                        rect.Rect = new Rect(rect.Rect.X * scaleX, rect.Rect.Y * scaleY, rect.Rect.Width * scaleX,
                            rect.Rect.Height * scaleY);
                        break;
                    case EllipseGeometry ellipse:
                        ellipse.Center = new Point(ellipse.Center.X * scaleX, ellipse.Center.Y * scaleY);
                        ellipse.RadiusX *= scaleX;
                        ellipse.RadiusY *= scaleY;
                        break;
                }
            }
        }
    }

    public Bitmap CombineCanvasWithBitmap(Canvas canvas, Bitmap originalBitmap)
    {
        if (canvas.Bounds.Width <= 0 || canvas.Bounds.Height <= 0)
        {
            Log.Error("The Canvas must have a defined size.");
            return originalBitmap;
        }

        var renderTargetBitmap = new RenderTargetBitmap(originalBitmap.PixelSize);
        using var drawingContext = renderTargetBitmap.CreateDrawingContext();
        // using var drawingContext2 = renderTargetBitmap.CreateDrawingContext();
        //先绘制原图
        // drawingContext.DrawImage(originalBitmap,
        //     new Rect(0, 0, originalBitmap.PixelSize.Width, originalBitmap.PixelSize.Height));
        // 绘制 Canvas
        // canvas.Measure(new Size(canvas.Bounds.Width, canvas.Bounds.Height));
        // canvas.Arrange(new Rect(0, 0, canvas.Bounds.Width, canvas.Bounds.Height));
        // canvas.Render(drawingContext2);
        canvas.Arrange(new Rect(0, 0, canvas.Bounds.Width, canvas.Bounds.Height));
        renderTargetBitmap.Render(canvas);
        return renderTargetBitmap;
    }
    //=================

    private void SafeClose()
    {
        if (_source != null)
        {
            var combinedBitmap = RenderToBitmap(DrawingCanvas, _source);
            _onClose.Invoke(combinedBitmap);
            ImageContent.Source = null;
            _source = null;
        }

        Close();
    }
}