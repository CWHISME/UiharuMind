using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.SemanticKernel;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Input;
using UiharuMind.Utils;
using UiharuMind.ViewModels.ScreenCaptures;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows.ScreenCapture;

public partial class ScreenCapturePreviewWindow : UiharuWindowBase
{
    private Point _dragStartPoint;
    private bool _isDragging;
    private Size _originSize;
    // private double _minScale;

    public Bitmap ImageSource;

    public ScreenCapturePreviewWindow()
    {
        InitializeComponent();

        //SizeToContent = SizeToContent.WidthAndHeight;

        this.SetSimpledecorationWindow();

        this.MinWidth = 30;
        this.MinHeight = 30;

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerWheelChanged += OnPointerWheelChangedEvent;
        PointerEntered += OnMouseEnter;
        // PointerExited += OnMouseLeave;
    }

    private void OnMouseEnter(object? sender, PointerEventArgs e)
    {
        ScreenCaptureManager.SyncDockWindow(this);
    }

    // private void OnMouseLeave(object? sender, PointerEventArgs e)
    // {
    //     ScreenCaptureManager.SyncBreakDockWindow(this);
    // }

    public void SetImage(Bitmap image, Size? size = null)
    {
        // Content = new Image { Source = image };
        var scaling = App.ScreensService.Scaling;
        _originSize = size ?? image.PixelSize.ToSize(scaling);
        // _minScale = Math.Min(100.0 / _originSize.Width, 100.0 / _originSize.Height);

        ImageSource = image;
        ImageContent.Source = image;

        var bounds = App.ScreensService.MouseScreen?.Bounds;
        if (bounds != null)
        {
            MaxWidth = bounds.Value.Width / scaling;
            MaxHeight = bounds.Value.Height / scaling;
        }

        SetImageSize(_originSize);

        SetLocation();
    }

    /// <summary>
    /// 在屏幕显示一张图(当前鼠标位置)
    /// </summary>
    /// <param name="image"></param>
    /// <param name="size">默认大小</param>
    public static void ShowWindowAtMousePosition(Bitmap? image, Size? size = null)
    {
        if (image == null)
        {
            Log.Error("image is null");
            return;
        }

        if (image.PixelSize.Width < 5 || image.PixelSize.Height < 5)
        {
            Log.Error("image PixelSize is too small");
            return;
        }

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var window = new ScreenCapturePreviewWindow();
            window.SetImage(image, size);
            window.Show();
        });
    }

    private void SetLocation()
    {
        this.SetWindowToMousePosition();
    }


    private const float MinScale = 0.20f;
    private const float MaxScale = 12.0f;
    private const float ScaleStep = 0.1f;
    private float _currentScale = 1.0f;

    private Size _currentSize;
    // private PixelPoint _currentPixelPoint;

    private void OnPointerWheelChangedEvent(object? sender, PointerWheelEventArgs e)
    {
        var mousePosition = e.GetPosition(ImageContent);
        var curPos = Position;

        if (e.Delta.Y != 0)
        {
            // 计算新的缩放比例
            // float sign = Math.Sign(e.Delta.Y);
            var newScale = (float)(_currentScale * (1 + e.Delta.Y * ScaleStep));

            // 限制缩放比例在最小和最大值之间
            if (newScale < MinScale || newScale > MaxScale ||
                newScale > _currentScale &&
                (this._currentSize.Width > MaxWidth || this._currentSize.Height > MaxHeight))
            {
                return;
            }

            // if ((_currentSize.Width > Width || _currentSize.Height > Height) && newScale > _currentScale) return;
            // if ((_currentSize.Width < MinWidth || _currentSize.Height < MinHeight) && newScale < _currentScale) return;

            // 计算缩放前后鼠标位置的变化
            // var oldMousePos = new Point(mousePosition.X / _currentScale, mousePosition.Y / _currentScale);
            // var newMousePos = new Point(mousePosition.X / newScale, mousePosition.Y / newScale);

            // 更新当前缩放比例
            _currentScale = newScale;

            // Dispatcher.UIThread.Post(() =>
            // {
            // 调整窗口大小以适应新的内容大小
            var newWidth = Math.Clamp(_originSize.Width * _currentScale, 0, MaxWidth);
            var newHeight = Math.Clamp(_originSize.Height * _currentScale, 0, MaxHeight);

            // // 计算图像宽度和高度的变化量
            // var widthChange = newWidth - _currentSize.Width;
            // var heightChange = newHeight - _currentSize.Height;
            // // if (sign > 0)
            // {
            //     widthChange *= 0.5f;
            //     heightChange *= 0.5f;
            // }

            // 计算新的窗口位置
            double zoomX = newWidth / _currentSize.Width;
            double zoomY = newHeight / _currentSize.Height;

            //调整窗口位置
            int newPosX = (int)(curPos.X - (mousePosition.X * (zoomX - 1)));
            int newPosY = (int)(curPos.Y - (mousePosition.Y * (zoomY - 1)));

            var pos = new PixelPoint(newPosX, newPosY);
            var size = new Size((int)newWidth, (int)newHeight);

            //确保鼠标位置在缩放后不超出界面
            pos += UiUtils.EnsureMousePositionWithinTargetOffset(pos, size);

            Dispatcher.UIThread.Invoke(() =>
            {
                // StopRendering();
                this.Position = pos;
                SetImageSize(size);
                // StartRendering();
                InvalidateMeasure();
            }, DispatcherPriority.Render);
            // });

            e.Handled = true;
        }
    }

    private void SetImageSize(Size newSize)
    {
        _currentSize = newSize;
        this.Width = newSize.Width;
        this.Height = newSize.Height;
        // ClientSize = newSize;
    }


    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            Close();
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragStartPoint = e.GetPosition(this);
            _isDragging = true;
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDragging)
        {
            var position = e.GetPosition(this);
            var diff = position - _dragStartPoint;

            var windowPosition = this.Position;
            windowPosition = new PixelPoint(
                (int)Math.Round(windowPosition.X + diff.X),
                (int)Math.Round(windowPosition.Y + diff.Y)
            );
            // Log.Debug($"windowPosition: {windowPosition}");
            this.Position = windowPosition;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDragging = false;
    }

    // protected override void OnClosing(WindowClosingEventArgs e)
    // {
    //     e.Cancel = true;
    // }
}