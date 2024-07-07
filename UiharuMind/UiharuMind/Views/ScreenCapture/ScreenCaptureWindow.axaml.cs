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

    // private int _screenWidth;
    // private int _screenHeight;
    private Screen? _currentScreen;

    private bool _error = false;

    public ScreenCaptureWindow()
    {
        InitializeComponent();
        InitializeWindow();

        // SelectionRectangle.Fill =new SolidColorBrush(Color.FromArgb(200,200 ,200, 100));
        // InfoPanel.Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));

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
        UpdateCaptureScreen();
        if (_currentScreen == null)
        {
            _error = true;
            return;
        }

        SystemDecorations = SystemDecorations.None;
        // Background = Brushes.Transparent;
        CanResize = false;
        // Topmost = true; // 确保窗口在最顶层
        ExtendClientAreaToDecorationsHint = true; // 扩展客户端区¸域以包括装饰
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
        WindowState = WindowState.FullScreen; // 最大化窗口

        // IntPtr hWnd = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        // if (hWnd == IntPtr.Zero)
        // {
        //     Log.Error("Failed to get window handle");
        //     _error = true;
        //     return;
        // }

        // IntPtr hWndInsertAfter = new IntPtr(-1);
        // int x = 0;
        // int y = 0;
        // if (Screens.Primary != null)
        // {
        //     var screen = Screens.ScreenFromWindow(this);
        //     if (screen != null)
        //     {
        //         try
        //         {
        //             _screenWidth = screen.Bounds.Width;
        //             _screenHeight = screen.Bounds.Height;
        //             // SetWindowPos(hWnd, hWndInsertAfter, x, y, _screenWidth, _screenHeight, 0);
        //             _screenWidth = (int)Math.Ceiling(_screenWidth / screen.Scaling);
        //             _screenHeight = (int)Math.Ceiling(_screenHeight / screen.Scaling);
        //         }
        //         catch (Exception e)
        //         {
        //             Log.Error(e.Message);
        //             // _error = true;
        //         }
        //     }
        // }

        // InfoPanel.IsVisible = false;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_error) Close();
        // CaptureScreen();
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void Canvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(ScreenshotCanvas).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
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
        if (!_isSelecting)
        {
            UpdateExtraInfo();
            return;
        }

        UpdateCaptureScreen();

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
        UpdateExtraInfo((int)width, (int)height, true);
    }

    private void Canvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isSelecting = false;
        // ScreenCapturePreviewWindow.ShowWindowAtMousePosition(await App.Clipboard.GetImageFromClipboard());
        Close();
    }

    private void UpdateExtraInfo()
    {
        if (_currentScreen == null) return;
        UpdateExtraInfo(_currentScreen.Bounds.Width, _currentScreen.Bounds.Height);
    }

    private void UpdateExtraInfo(int width, int height, bool correct = false)
    {
        // var screenBounds = this.Screens.ScreenFromWindow(this).Bounds;
        if (_currentScreen == null) return;
        try
        {
            var currentPosition = App.ScreensService.MousePosition;
            var controlSize = InfoPanel.Bounds.Size;
            int posX = Math.Clamp((int)(currentPosition.X), _currentScreen.Bounds.Position.X,
                _currentScreen.Bounds.Position.X + _currentScreen.Bounds.Width);
            int posY = Math.Clamp((int)(currentPosition.Y), _currentScreen.Bounds.Position.Y,
                _currentScreen.Bounds.Position.Y + _currentScreen.Bounds.Height);
            // 确保控件在屏幕内
            var x = Math.Max(0,
                Math.Min(posX / _currentScreen.Scaling,
                    _currentScreen.Bounds.Width / _currentScreen.Scaling - controlSize.Width));
            var y = Math.Max(0,
                Math.Min(posY / _currentScreen.Scaling + 20,
                    _currentScreen.Bounds.Height / _currentScreen.Scaling - controlSize.Height));
            // Log.Debug("SelectionRectangle: " + left + ", " + top + ", " + width + ", " + height + " x: " + x + " y: " + y +
            //           "screenBounds.Width" + _screenWidth + " screenBounds.Height" + _screenHeight +
            //           "controlSize.Width" + controlSize.Width + "controlSize.Height" + controlSize.Height);
            if (correct)
            {
                width = (int)Math.Ceiling(width * _currentScreen.Scaling);
                height = (int)Math.Ceiling(height * _currentScreen.Scaling);
            }

            PositionText.Text = "X: " + posX + " Y: " + posY;
            ResolutionText.Text = width + "x" + height;
            // Log.Debug("x: " + x + " y: " + y + "   currentPosition:" + currentPosition);
            InfoPanel.Margin = new Thickness(x, y, 0, 0);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    /// <summary>
    /// 动态切换了多屏，更新截图区域
    /// </summary>
    private void UpdateCaptureScreen()
    {
        var currentScreen = App.ScreensService.MouseScreen;
        if (currentScreen == _currentScreen || currentScreen == null) return;
        _currentScreen = currentScreen;
        var bounds = _currentScreen.Bounds;
        this.Width = bounds.Width;
        this.Height = bounds.Height;
        this.Position = bounds.Position;
    }

    private async void CaptureScreen()
    {
        await UiharuCoreManager.Instance.CaptureScreen(App.ScreensService.MouseScreenId);
        var image = await App.Clipboard.GetImageFromClipboard();
        ScreenshotImage.Source = image;
    }
}