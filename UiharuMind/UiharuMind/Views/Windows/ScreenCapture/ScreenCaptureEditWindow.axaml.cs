using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

public enum DrawingTool
{
    Rectangle,
    Circle,
    Line,
    ArrowLine,
    Text,
    Max
}

public partial class ScreenCaptureEditWindow : Window
{
    private Bitmap? _source;
    private Action<Bitmap> _onClose;
    private Size _size;
    // private double _scale;

    private Stack<Control> _undoStack = new Stack<Control>();
    private Stack<Control> _redoStack = new Stack<Control>();

    private DrawingTool _currentTool = DrawingTool.Rectangle;
    private bool _isDrawing;
    private Point _startPoint;
    private Control? _currentControl;
    private Color _currentColor = Colors.Red;
    private int _clickCount;
    private int _onTextLostFocusCount;
    private bool _isClose;

    public int BtnHeight { get; set; } = 45;

    // private KeyCombinationData _undoKey;
    // private KeyCombinationData _redoKey;

    public ScreenCaptureEditWindow(Bitmap source, PixelPoint position, Size size, Action<Bitmap> onClose)
    {
        _source = source;
        _onClose = onClose;
        _size = size;
        // _scale = size.Width / source.PixelSize.Width;
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

        InitializeEvents();
    }

    private void InitializeEvents()
    {
        GeometryRectangleButton.IsCheckedChanged += (s, e) => UpdateTool(s, DrawingTool.Rectangle);
        GeometryCircleButton.IsCheckedChanged += (s, e) => UpdateTool(s, DrawingTool.Circle);
        GeometryLineButton.IsCheckedChanged += (s, e) => UpdateTool(s, DrawingTool.Line);
        GeometryArrowLineButton.IsCheckedChanged += (s, e) => UpdateTool(s, DrawingTool.ArrowLine);
        GeometryTextButton.IsCheckedChanged += (s, e) => UpdateTool(s, DrawingTool.Text);

        GeometryRectangleButton.IsChecked = true;

        DrawingCanvas.PointerPressed += OnDrawingCanvasPointerPressed;
        DrawingCanvas.PointerMoved += OnDrawingCanvasPointerMoved;
        DrawingCanvas.PointerReleased += OnDrawingCanvasPointerReleased;

        GeometryColorPicker.ColorChanged += (s, e) => _currentColor = e.NewColor;
    }


    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ImageContent.Source = _source;
        ImageContent.Width = _size.Width;
        ImageContent.Height = _size.Height;
        DrawingCanvas.Width = _size.Width;
        DrawingCanvas.Height = _size.Height;

        GeometryColorPicker.Color = _currentColor;
        RefreshUndoRedo();

        Focusable = true;
        _isClose = false;
        Focus();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        PointerUpdateKind pointerUpdateKind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        if (pointerUpdateKind == PointerUpdateKind.LeftButtonPressed && e.ClickCount >= 2)
        {
            // Log.Debug("关闭" + _isDrawing + "  " + _currentControl);
            SafeClose();
        }
    }

    // private void CancelButton_Click(object? sender, RoutedEventArgs e)
    // {
    //     SafeClose();
    // }

    // private void SaveButton_Click(object? sender, RoutedEventArgs e)
    // {
    //     SafeClose();
    // }

    // private void OnGeometryRectangleButtonIsCheckedChanged(object? sender, RoutedEventArgs e)
    // {
    //     if (GeometryRectangleButton.IsChecked == true)
    //     {
    //         GeometryCircleButton.IsChecked = false;
    //     }
    //     //如果旁边未选中，那么不允许自己不选中
    //     else if (GeometryCircleButton.IsChecked == false)
    //     {
    //         GeometryRectangleButton.IsChecked = true;
    //     }
    // }

    // private void OnGeometryCircleButtonIsCheckedChanged(object? sender, RoutedEventArgs e)
    // {
    //     if (GeometryCircleButton.IsChecked == true)
    //     {
    //         GeometryRectangleButton.IsChecked = false;
    //     }
    //     //如果旁边未选中，那么不允许自己不选中
    //     else if (GeometryRectangleButton.IsChecked == false)
    //     {
    //         GeometryCircleButton.IsChecked = true;
    //     }
    // }

    [RelayCommand]
    private void Undo()
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
    private void Redo()
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

    #region Tool Management

    private void UpdateTool(object? sender, DrawingTool tool)
    {
        if (!(sender as ToggleButton)?.IsChecked ?? false)
        {
            if (tool == _currentTool) _currentTool = DrawingTool.Max;
            return;
        }

        _currentTool = tool;
        GeometryRectangleButton.IsChecked = tool == DrawingTool.Rectangle;
        GeometryCircleButton.IsChecked = tool == DrawingTool.Circle;
        GeometryLineButton.IsChecked = tool == DrawingTool.Line;
        GeometryArrowLineButton.IsChecked = tool == DrawingTool.ArrowLine;
        GeometryTextButton.IsChecked = tool == DrawingTool.Text;
        
        DrawingCanvas.Cursor = tool == DrawingTool.Text ? new Cursor(StandardCursorType.Ibeam) : Cursor.Default;
    }

    #endregion

    //绘制==============

    #region Drawing Operations

    private void OnDrawingCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pointerPoint = e.GetCurrentPoint(DrawingCanvas);
        if (!pointerPoint.Properties.IsLeftButtonPressed || _isDrawing)
            return;

        // Log.Debug("点击");

        _redoStack.Clear();
        _isDrawing = true;
        _startPoint = e.GetPosition(DrawingCanvas);
        _currentColor = GeometryColorPicker.Color;
        _clickCount++;

        switch (_currentTool)
        {
            case DrawingTool.Rectangle:
            case DrawingTool.Circle:
            case DrawingTool.Line:
            case DrawingTool.ArrowLine:
                CreateShape();
                break;

            case DrawingTool.Text:
                CreateTextControl();
                break;
        }
    }

    private void CreateShape()
    {
        Control? control = null;

        switch (_currentTool)
        {
            case DrawingTool.Rectangle:
                control = new Rectangle
                {
                    Stroke = new SolidColorBrush(_currentColor),
                    StrokeThickness = 2,
                    Fill = Brushes.Transparent
                };
                break;

            case DrawingTool.Circle:
                control = new Ellipse
                {
                    Stroke = new SolidColorBrush(_currentColor),
                    StrokeThickness = 2,
                    Fill = Brushes.Transparent
                };
                break;

            case DrawingTool.Line:
                control = new Line
                {
                    Stroke = new SolidColorBrush(_currentColor),
                    StrokeThickness = 2,
                    StartPoint = _startPoint,
                    EndPoint = _startPoint
                };
                break;
            case DrawingTool.ArrowLine:
                control = new ArrowLineControl(_startPoint, _startPoint, _currentColor);
                break;
        }

        if (control != null)
        {
            DrawingCanvas.Children.Add(control);
            _undoStack.Push(control);
            _currentControl = control;
            RefreshUndoRedo();
        }
    }

    private void CreateTextControl()
    {
        if (_onTextLostFocusCount == _clickCount)
        {
            _currentControl = null;
            _isDrawing = false;
            return;
        }

        var textBox = new TextBox
        {
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(_currentColor),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Colors.Gray),
            FontSize = 16,
            AcceptsReturn = false,
            Text = ""
        };

        Canvas.SetLeft(textBox, _startPoint.X);
        Canvas.SetTop(textBox, _startPoint.Y - 16);

        textBox.LostFocus += (s, e) => ConvertTextBoxToTextBlock(textBox);
        textBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                ConvertTextBoxToTextBlock(textBox);
            }
        };

        DrawingCanvas.Children.Add(textBox);
        textBox.Focus();
        textBox.SelectAll();
        _currentControl = textBox;
        _isDrawing = false;
    }

    private void ConvertTextBoxToTextBlock(TextBox textBox)
    {
        _onTextLostFocusCount = _clickCount + 1;
        // Log.Debug("ConvertTextBoxToTextBlock" + _onTextLostFocusCount);
        if (_isClose || string.IsNullOrEmpty(textBox.Text))
        {
            DrawingCanvas.Children.Remove(textBox);
            return;
        }

        _currentControl = null;
        var textBlock = new TextBlock
        {
            Text = textBox.Text,
            Foreground = textBox.Foreground,
            FontSize = textBox.FontSize,
            FontWeight = FontWeight.Bold
        };

        Canvas.SetLeft(textBlock, Canvas.GetLeft(textBox) + 9);
        Canvas.SetTop(textBlock, Canvas.GetTop(textBox) + 7);

        DrawingCanvas.Children.Remove(textBox);
        DrawingCanvas.Children.Add(textBlock);

        _undoStack.Push(textBlock);
        RefreshUndoRedo();
    }

    private void OnDrawingCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDrawing || _currentControl == null)
            return;

        var currentPoint = e.GetPosition(DrawingCanvas);
        var bounds = GetDrawingBounds();

        currentPoint = ConstrainPointToBounds(currentPoint, bounds);

        switch (_currentTool)
        {
            case DrawingTool.Rectangle when _currentControl is Rectangle rectangle:
                UpdateRectangle(rectangle, currentPoint, bounds);
                break;

            case DrawingTool.Circle when _currentControl is Ellipse ellipse:
                UpdateEllipse(ellipse, currentPoint, bounds);
                break;

            case DrawingTool.Line when _currentControl is Line line:
                UpdateLine(line, currentPoint, bounds);
                break;
            
            case DrawingTool.ArrowLine when _currentControl is ArrowLineControl arrowLine:
                arrowLine.UpdateEndPoint(currentPoint);
                break;
        }
    }

    private void UpdateRectangle(Rectangle rectangle, Point currentPoint, Rect bounds)
    {
        var (left, top, width, height) = CalculateRectDimensions(_startPoint, currentPoint, bounds);

        Canvas.SetLeft(rectangle, left);
        Canvas.SetTop(rectangle, top);
        rectangle.Width = width;
        rectangle.Height = height;
    }

    private void UpdateEllipse(Ellipse ellipse, Point currentPoint, Rect bounds)
    {
        var constrainedStart = ConstrainPointToBounds(_startPoint, bounds);
        var constrainedEnd = ConstrainPointToBounds(currentPoint, bounds);

        // 计算椭圆的中心点和半径
        var centerX = (constrainedStart.X + constrainedEnd.X) / 2;
        var centerY = (constrainedStart.Y + constrainedEnd.Y) / 2;
        var radiusX = Math.Abs(constrainedEnd.X - constrainedStart.X) / 2;
        var radiusY = Math.Abs(constrainedEnd.Y - constrainedStart.Y) / 2;

        Canvas.SetLeft(ellipse, centerX - radiusX);
        Canvas.SetTop(ellipse, centerY - radiusY);
        ellipse.Width = radiusX * 2;
        ellipse.Height = radiusY * 2;
    }

    private void UpdateLine(Line line, Point currentPoint, Rect bounds)
    {
        // var startPoint = ConstrainPointToBounds(_startPoint, bounds);
        var endPoint = ConstrainPointToBounds(currentPoint, bounds);

        // line.StartPoint = startPoint;
        line.EndPoint = endPoint;
    }

    private void OnDrawingCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDrawing && _currentTool != DrawingTool.Text)
        {
            _isDrawing = false;
            _currentControl = null;
        }
    }

    #endregion

    #region Utility Methods

    private (double left, double top, double width, double height) CalculateRectDimensions(
        Point start, Point end, Rect bounds)
    {
        var left = Math.Max(Math.Min(start.X, end.X), bounds.Left);
        var top = Math.Max(Math.Min(start.Y, end.Y), bounds.Top);
        var right = Math.Min(Math.Max(start.X, end.X), bounds.Right);
        var bottom = Math.Min(Math.Max(start.Y, end.Y), bounds.Bottom);

        var width = Math.Max(0, right - left);
        var height = Math.Max(0, bottom - top);

        return (left, top, width, height);
    }

    private Rect GetDrawingBounds()
    {
        const int borderX = 2;
        const int borderY = 1;
        return new Rect(
            DrawingCanvas.Bounds.X + borderX,
            DrawingCanvas.Bounds.Y + borderY,
            DrawingCanvas.Bounds.Width - borderX * 2,
            DrawingCanvas.Bounds.Height - borderY * 2);
    }

    private Point ConstrainPointToBounds(Point point, Rect bounds)
    {
        return new Point(
            Math.Clamp(point.X, bounds.Left, bounds.Right),
            Math.Clamp(point.Y, bounds.Top, bounds.Bottom));
    }

    #endregion

    // private bool _isDrawing;
    // private Point _startPoint;
    // private Geometry? _currentGeometry;

    // private void OnDrawingCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    // {
    //     if (e.GetCurrentPoint(DrawingCanvas).Properties.IsLeftButtonPressed)
    //     {
    //         _redoStack.Clear();
    //
    //         _isDrawing = true;
    //         _startPoint = e.GetPosition(DrawingCanvas);
    //         _currentGeometry = GeometryRectangleButton.IsChecked == true
    //             ? new RectangleGeometry(new Rect(_startPoint, new Size(0, 0)))
    //             : new EllipseGeometry();
    //
    //         _color = GeometryColorPicker.Color;
    //         var path = new Path
    //         {
    //             Data = _currentGeometry,
    //             Fill = Brushes.Transparent,
    //             StrokeThickness = 2,
    //             Stroke = new SolidColorBrush(_color)
    //         };
    //         DrawingCanvas.Children.Add(path);
    //         _undoStack.Push(path);
    //         RefreshUndoRedo();
    //     }
    // }

    // private void OnDrawingCanvasPointerMoved(object? sender, PointerEventArgs e)
    // {
    //     if (_isDrawing && DrawingCanvas != null)
    //     {
    //         const int borderX = 2;
    //         const int borderY = 1;
    //         var canvasBounds = new Rect(DrawingCanvas.Bounds.X + borderX, DrawingCanvas.Bounds.Y + borderY,
    //             DrawingCanvas.Bounds.Width - borderX,
    //             DrawingCanvas.Bounds.Height - borderX);
    //         var position = e.GetPosition(DrawingCanvas);
    //
    //         position = new Point(
    //             Math.Max(Math.Min(position.X, canvasBounds.Right), canvasBounds.Left),
    //             Math.Max(Math.Min(position.Y, canvasBounds.Bottom), canvasBounds.Top));
    //
    //         switch (_currentGeometry)
    //         {
    //             case RectangleGeometry rect:
    //                 var startPoint = _startPoint;
    //
    //                 double left = Math.Min(startPoint.X, position.X);
    //                 double top = Math.Min(startPoint.Y, position.Y);
    //                 double right = Math.Max(startPoint.X, position.X);
    //                 double bottom = Math.Max(startPoint.Y, position.Y);
    //
    //                 left = Math.Max(left, canvasBounds.Left);
    //                 top = Math.Max(top, canvasBounds.Top);
    //                 right = Math.Min(right, canvasBounds.Right);
    //                 bottom = Math.Min(bottom, canvasBounds.Bottom);
    //
    //                 rect.Rect = new Rect(left, top, right - left, bottom - top);
    //                 break;
    //             case EllipseGeometry ellipse:
    //                 var center = _startPoint;
    //                 double radiusX = Math.Abs(position.X - center.X);
    //                 double radiusY = Math.Abs(position.Y - center.Y);
    //
    //                 if (position.X < center.X)
    //                 {
    //                     center = new Point(center.X - Math.Min(radiusX, center.X - canvasBounds.Left), center.Y);
    //                     radiusX = Math.Min(radiusX, center.X - canvasBounds.Left);
    //                 }
    //                 else
    //                 {
    //                     center = new Point(center.X + Math.Min(radiusX, canvasBounds.Right - center.X), center.Y);
    //                     radiusX = Math.Min(radiusX, canvasBounds.Right - center.X);
    //                 }
    //
    //                 if (position.Y < center.Y)
    //                 {
    //                     center = new Point(center.X, center.Y - Math.Min(radiusY, center.Y - canvasBounds.Top));
    //                     radiusY = Math.Min(radiusY, center.Y - canvasBounds.Top);
    //                 }
    //                 else
    //                 {
    //                     center = new Point(center.X, center.Y + Math.Min(radiusY, canvasBounds.Bottom - center.Y));
    //                     radiusY = Math.Min(radiusY, canvasBounds.Bottom - center.Y);
    //                 }
    //
    //                 ellipse.Center = center;
    //                 ellipse.RadiusX = radiusX;
    //                 ellipse.RadiusY = radiusY;
    //                 // Log.Debug($"center:{center}, radiusX:{radiusX}, radiusY:{radiusY}");
    //                 break;
    //         }
    //     }
    // }

    // private void OnDrawingCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    // {
    //     if (_isDrawing)
    //     {
    //         _isDrawing = false;
    //         // CombineCanvasWithBitmap(DrawingCanvas, _source);
    //     }
    // }

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
        _isClose = true;

        if (_currentControl != null)
        {
            DrawingCanvas.Children.Remove(_currentControl);
            _currentControl = null;
        }

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