using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using UiharuMind.Core;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Views.Capture;

public partial class ScreenCaptureWindow : Window
{
    private Point _startPoint;
    private bool _isSelecting;
    private int _screenWidth;
    private int _screenHeight;

    private bool _error = false;

    public ScreenCaptureWindow()
    {
        InitializeComponent();
        InitializeWindow();

        PointerPressed += Canvas_PointerPressed;
        PointerMoved += Canvas_PointerMoved;
        PointerReleased += Canvas_PointerReleased;
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy,
        int uFlags);

    private void InitializeWindow()
    {
        CaptureScreen();
        var bounds = App.ScreensService.MouseScreen.Bounds;
        this.Width = bounds.Width;
        this.Height = bounds.Height;
        // this.Position = bounds.Position;

        ShowActivated = false;
        SystemDecorations = SystemDecorations.None;
        // Background = Brushes.Transparent;
        CanResize = false;
        Topmost = true; // 确保窗口在最顶层
        ExtendClientAreaToDecorationsHint = true; // 扩展客户端区¸域以包括装饰
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
        WindowState = WindowState.FullScreen; // 最大化窗口

        IntPtr hWnd = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hWnd == IntPtr.Zero)
        {
            Log.Error("Failed to get window handle");
            _error = true;
            return;
        }

        IntPtr hWndInsertAfter = new IntPtr(-1);
        int x = 0;
        int y = 0;
        if (Screens.Primary != null)
        {
            var screen = Screens.ScreenFromWindow(this);
            if (screen != null)
            {
                try
                {
                    _screenWidth = screen.Bounds.Width;
                    _screenHeight = screen.Bounds.Height;
                    SetWindowPos(hWnd, hWndInsertAfter, x, y, _screenWidth, _screenHeight, 0);
                    _screenWidth = (int)Math.Ceiling(_screenWidth / screen.Scaling);
                    _screenHeight = (int)Math.Ceiling(_screenHeight / screen.Scaling);
                }
                catch (Exception e)
                {
                    Log.Error(e.Message);
                    // _error = true;
                }
            }
        }

        InfoPanel.Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
        // InfoPanel.IsVisible = false;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_error) Close();
        // CaptureScreen();
    }

    private void Canvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
        {
            _isSelecting = true;
            _startPoint = e.GetPosition(ScreenshotCanvas);
            SelectionRectangle.Width = 0;
            SelectionRectangle.Height = 0;
            // InfoPanel.IsVisible = true;
            Canvas.SetLeft(SelectionRectangle, _startPoint.X);
            Canvas.SetTop(SelectionRectangle, _startPoint.Y);
        }
    }

    private void Canvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        // if (!_isSelecting) return;
        var currentPosition = e.GetPosition(ScreenshotCanvas);
        var width = Math.Ceiling(Math.Abs(currentPosition.X - _startPoint.X));
        var height = Math.Ceiling(Math.Abs(currentPosition.Y - _startPoint.Y));
        var left = Math.Ceiling(Math.Min(_startPoint.X, currentPosition.X));
        var top = Math.Ceiling(Math.Min(_startPoint.Y, currentPosition.Y));
        SelectionRectangle.Width = width;
        SelectionRectangle.Height = height;
        Canvas.SetLeft(SelectionRectangle, left);
        Canvas.SetTop(SelectionRectangle, top);

        //==额外信息==
        // var screenBounds = this.Screens.ScreenFromWindow(this).Bounds;
        var controlSize = InfoPanel.Bounds.Size;
        // 确保控件在屏幕内
        var x = Math.Max(0, Math.Min(currentPosition.X, _screenWidth - controlSize.Width));
        var y = Math.Max(0, Math.Min(currentPosition.Y + 20, _screenHeight - controlSize.Height));
        // Log.Debug("SelectionRectangle: " + left + ", " + top + ", " + width + ", " + height + " x: " + x + " y: " + y +
        //           "screenBounds.Width" + _screenWidth + " screenBounds.Height" + _screenHeight +
        //           "controlSize.Width" + controlSize.Width + "controlSize.Height" + controlSize.Height);
        ResolutionText.Text = width + "x" + height;

        InfoPanel.Margin = new Thickness(x, y, 0, 0);
    }

    private async void Canvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isSelecting = false;
        // ScreenCapturePreviewWindow.ShowWindowAtMousePosition(await App.Clipboard.GetImageFromClipboard());
        Close();
    }

    private async void CaptureScreen()
    {
        await UiharuCoreManager.Instance.CaptureScreen(App.ScreensService.MouseScreenId);
        var image = await App.Clipboard.GetImageFromClipboard();
        ScreenshotImage.Source = image;
    }
}