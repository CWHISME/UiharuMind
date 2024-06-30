using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using UiharuMind.Core.Input;

namespace UiharuMind.Views.Capture;

public partial class ScreenCaptureWindow : Window
{
    private Point _dragStartPoint;
    private bool _isDragging;

    public ScreenCaptureWindow()
    {
        InitializeComponent();

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerWheelChanged += OnPointerWheelChangedEvent;
    }

    public void SetImage(Bitmap image)
    {
        Topmost = true;
        Content = new Image { Source = image };
        Width = image.Size.Width;
        Height = image.Size.Height;
        WindowState = WindowState.Normal;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // CanResize = false;
        SystemDecorations = SystemDecorations.BorderOnly;

        SetLocation();
    }

    /// <summary>
    /// 在屏幕显示一张图(当前鼠标位置)
    /// </summary>
    /// <param name="image"></param>
    public static void ShowWindowAtMousePosition(Bitmap image)
    {
        var window = new ScreenCaptureWindow();
        window.SetImage(image);
        window.Show();
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

    private void OnPointerWheelChangedEvent(object sender, PointerWheelEventArgs e)
    {
        const float scale = 1.1f;
        const float minScale = 0.5f; // 最小缩放比例
        var windowSize = this.ClientSize;
        var windowPosition = this.Position;
        var mousePosition = e.GetPosition(this);

        if (e.Delta.Y != 0)
        {
            var scaleFactor = e.Delta.Y > 0 ? scale : 1 / scale;

            // 确保新大小不会小于最小值
            var newWidth = Math.Max(windowSize.Width * scaleFactor, windowSize.Width * minScale);
            var newHeight = Math.Max(windowSize.Height * scaleFactor, windowSize.Height * minScale);

            this.Position = new PixelPoint(
                (int)(windowPosition.X - (mousePosition.X * scaleFactor - mousePosition.X)),
                (int)(windowPosition.Y - (mousePosition.Y * scaleFactor - mousePosition.Y))
            );

            this.ClientSize = new Size(newWidth, newHeight);

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
            this.Position = windowPosition;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDragging = false;
    }
}