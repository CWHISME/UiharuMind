using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Input;
using UiharuMind.ViewModels.ScreenCaptures;

namespace UiharuMind.Views.Capture;

public partial class ScreenCapturePreviewWindow : Window
{
    private Point _dragStartPoint;
    private bool _isDragging;
    private PixelSize _originSize;
    private float _minScale;

    public Bitmap ImageSource;

    public ScreenCapturePreviewWindow()
    {
        InitializeComponent();

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

    public void SetImage(Bitmap image)
    {
        Topmost = true;
        // Content = new Image { Source = image };
        _originSize = image.PixelSize;
        _minScale = Math.Min(100.0f / _originSize.Width, 100.0f / _originSize.Height);

        ImageSource = image;
        ImageContent.Source = image;
        Width = image.Size.Width;
        Height = image.Size.Height;
        WindowState = WindowState.Normal;
        WindowStartupLocation = WindowStartupLocation.Manual;
        CanResize = false;
        SystemDecorations = SystemDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
        ExtendClientAreaTitleBarHeightHint = -1;

        SetLocation();
    }

    /// <summary>
    /// 在屏幕显示一张图(当前鼠标位置)
    /// </summary>
    /// <param name="image"></param>
    public static void ShowWindowAtMousePosition(Bitmap? image)
    {
        if (image == null)
        {
            Log.Error("image is null");
            return;
        }

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var window = new ScreenCapturePreviewWindow();
            window.SetImage(image);
            window.Show();
        });
    }

    private void SetLocation()
    {
        var pos = new PixelPoint(InputManager.MouseData.X, InputManager.MouseData.Y);
        // var pointerPosition = window.PointToScreen(new Point(0, 0));
        // // 将窗口的右下角定位在鼠标位置
        // window.Position = new PixelPoint((int)(pointerPosition.X - window.Width),
        //     (int)(pointerPosition.Y - window.Height));
        // window.Position = pos;
        Position = new PixelPoint((int)(pos.X - Width),
            (int)(pos.Y - Height));
    }

    private void OnPointerWheelChangedEvent(object? sender, PointerWheelEventArgs e)
    {
        const float scale = 1.1f;
        // const float minScale = 0.5f; // 最小缩放比例
        var windowSize = new Size(this.Width, this.Height);
        var windowPosition = this.Position;
        var mousePosition = e.GetPosition(this);

        if (e.Delta.Y != 0)
        {
            var scaleFactor = e.Delta.Y > 0 ? scale : 1 / scale;

            // 计算当前的缩放比例
            var currentScale = Math.Min((float)windowSize.Width / _originSize.Width,
                (float)windowSize.Height / _originSize.Height);

            // 如果当前缩放比例已经小于或等于最小缩放比例，并且正在尝试缩小，则不进行缩放
            if (currentScale <= _minScale && e.Delta.Y < 0)
            {
                return;
            }

            // 计算新的窗口尺寸
            var newWidth = (windowSize.Width * scaleFactor);
            var newHeight = (windowSize.Height * scaleFactor);
            // 确保新的尺寸保持原始宽高比例
            var aspectRatio = _originSize.Width / (float)_originSize.Height;
            if (newWidth / aspectRatio < newHeight)
            {
                newHeight = (newWidth / aspectRatio);
            }
            else
            {
                newWidth = (newHeight * aspectRatio);
            }

            Dispatcher.UIThread.Post(() =>
            {
                // 设置新的窗口尺寸
                this.Width = newWidth;
                this.Height = newHeight;
                // 调整窗口位置以保持鼠标位置不变
                this.Position = new PixelPoint(
                    (int)(windowPosition.X - (mousePosition.X * scaleFactor - mousePosition.X)),
                    (int)(windowPosition.Y - (mousePosition.Y * scaleFactor - mousePosition.Y))
                );
            });

            e.Handled = true;
        }
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