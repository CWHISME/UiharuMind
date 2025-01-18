/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using HPPH;
using UiharuMind.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.UiharuScreenCapture;
using UiharuMind.Utils;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows.ScreenCapture;

public partial class ScreenCaptureWindow : UiharuWindowBase
{
    private Point _startPoint;

    private bool _isSelecting;

    // private int _screenWidth;
    // private int _screenHeight;
    private Screen? _currentScreen;
    private IImage? _image;

    // private bool _error = false;

    public ScreenCaptureWindow()
    {
        InitializeComponent();
        InitializeWindow();

        // SelectionRectangle.Fill =new SolidColorBrush(Color.FromArgb(200,200 ,200, 100));
        // InfoPanel.Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));

        // this.GetObservable(IsVisibleProperty).Subscribe(new VisibilityObserver(this));
    }

    // private class VisibilityObserver : IObserver<bool>
    // {
    //     private readonly ScreenCaptureWindow _control;
    //
    //     public VisibilityObserver(ScreenCaptureWindow control)
    //     {
    //         _control = control;
    //     }
    //
    //     public void OnNext(bool value)
    //     {
    //         if (value)
    //         {
    //             // 当 UserControl 变为可见时执行的代码
    //             Log.Debug("UserControl is now visible.");
    //             _control.ClearData();
    //             
    //         }
    //         else
    //         {
    //             // 当 UserControl 变为不可见时执行的代码
    //             Log.Debug("UserControl is no longer visible.");
    //         }
    //     }
    //
    //     public void OnError(Exception error)
    //     {
    //         Log.Error($"An error occurred: {error.Message}");
    //     }
    //
    //     public void OnCompleted()
    //     {
    //         Log.Debug("Observation completed.");
    //     }
    // }


    // [DllImport("user32.dll")]
    // private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy,
    //     int uFlags);

    private void InitializeWindow()
    {
        SystemDecorations = SystemDecorations.None;
        // Background = Brushes.Transparent;
        CanResize = false;
        Topmost = true; // 确保窗口在最顶层
        ShowInTaskbar = false;
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

    protected override void OnPreShow()
    {
        UpdateCaptureScreen();
    }

    // protected override void OnOpened(EventArgs e)
    // {
    //     base.OnOpened(e);
    //     // UpdateCaptureScreen();
    //     // CaptureScreen();
    //     Log.Debug("OnOpened.");
    // }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    protected override void OnPreClose()
    {
        _currentScreen = null;
        ClearData();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        PointerUpdateKind pointerUpdateKind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        if (pointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
        {
            _isSelecting = true;
            _startPoint = e.GetPosition(ScreenshotCanvas);
            SelectionRectangle.Width = 0;
            SelectionRectangle.Height = 0;
            // InfoPanel.IsVisible = true;
            Canvas.SetLeft(SelectionRectangle, _startPoint.X);
            Canvas.SetTop(SelectionRectangle, _startPoint.Y);
        }
        else if (pointerUpdateKind == PointerUpdateKind.RightButtonPressed)
        {
            Close();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_isSelecting)
        {
            UpdateExtraInfo();
            return;
        }

        if (_currentScreen == null) return;

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

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        // App.ScreensService.MousePosition = PixelPoint.FromPoint(e.GetPosition(this), App.ScreensService.Scaling);
        if (_isSelecting) DoAreaCapture();
    }

    private void UpdateExtraInfo()
    {
        if (_currentScreen == null) return;
        UpdateExtraInfo(_currentScreen.Bounds.Width, _currentScreen.Bounds.Height);
    }

    private void UpdateExtraInfo(int width, int height, bool correct = false)
    {
        if (_currentScreen == null) return;
        try
        {
            var position = UiUtils.EnsurePositionWithinScreen(_currentScreen, App.ScreensService.MousePosition,
                InfoPanel.Bounds.Size);

            if (correct)
            {
                width = (int)Math.Ceiling(width * _currentScreen.Scaling);
                height = (int)Math.Ceiling(height * _currentScreen.Scaling);
            }

            // PixelPoint pixelPoint = PixelPoint.FromPoint(point, _currentScreen.Scaling);
            var mousePosition = App.ScreensService.MousePosition;
            PositionText.Text =
                $"position:({Math.Clamp(mousePosition.X, 0, _currentScreen.Bounds.Width)},{Math.Clamp(mousePosition.Y, 0, _currentScreen.Bounds.Height)})";
            ResolutionText.Text = $"resolution:({width}x{height})";
            // TipsText.Text = $"{point.X} {point.Y}";
            Point point = position.ToPoint(_currentScreen.Scaling);
            // Log.Debug($"position:({position.X},{position.Y}) point:({point.X},{point.Y})");
            InfoPanel.Margin = new Thickness(Math.Floor(point.X), Math.Floor(point.Y), 0, 0);
        }
        catch (Exception e)
        {
            Log.Error(e.StackTrace);
        }
    }

    /// <summary>
    /// 动态切换了多屏，更新截图区域
    /// </summary>
    private async void UpdateCaptureScreen()
    {
        var currentScreen = App.ScreensService.MouseScreen;
        if (currentScreen == _currentScreen || currentScreen == null) return;
        //清理当前数据
        // Log.Debug("清理当前数据");
        ClearData();
        // Log.Debug("更新截图");
        //截屏
        CaptureScreen();
        // Log.Debug("截图完成");
        //更新截图数据
        _currentScreen = currentScreen;
        var bounds = _currentScreen.Bounds;
        this.Width = bounds.Width;
        this.Height = bounds.Height;
        this.Position = bounds.Position;
        //展示截图
        // Log.Debug("展示截图");
        DisplayCapture();
    }

    private void ClearData()
    {
        // PointerPressed -= Canvas_PointerPressed;
        // PointerMoved -= Canvas_PointerMoved;
        // PointerReleased -= Canvas_PointerReleased;

        SelectionRectangle.Width = 0;
        SelectionRectangle.Height = 0;
        Canvas.SetLeft(SelectionRectangle, 0);
        Canvas.SetTop(SelectionRectangle, 0);
        InfoPanel.IsVisible = false;
        MainPanel.IsVisible = false;
        ScreenshotImage.Source = null;
        _startPoint = new Point(0, 0);
        _isSelecting = false;
        _currentScreen = null;
    }

    private void DisplayCapture()
    {
        MainPanel.IsVisible = true;
        InfoPanel.IsVisible = true;
        UpdateExtraInfo();
    }

    /// <summary>
    /// 执行全屏截图
    /// </summary>
    private void CaptureScreen()
    {
        ScreenshotImage.Source = null;

        _image = ScreenCaptureWin.Capture(App.ScreensService.MouseScreenIndex);
        if (_image == null)
        {
            Log.Error("Failed to capture screen");
            Close();
            return;
        }

        var image = _image.ImageToBitmap(); //await App.Clipboard.GetImageFromClipboard();
        if (image == null)
        {
            Log.Error("Failed to convert capture screen image to bitmap");
            Close();
            return;
        }

        ScreenshotImage.Source = image;
    }

    /// <summary>
    /// 执行区域截图，完毕后关闭界面，并弹出预览窗口
    /// </summary>
    private void DoAreaCapture()
    {
        _isSelecting = false;
        if (_image is { Width: > 0, Height: > 0 } && _currentScreen != null && SelectionRectangle.Width > 0 &&
            SelectionRectangle.Height > 0)
        {
            try
            {
                PixelPoint startPixelPoint = PixelPoint.FromPoint(_startPoint, _currentScreen.Scaling);
                var childImage = _image[startPixelPoint.X, startPixelPoint.Y,
                    (int)(SelectionRectangle.Width * _currentScreen.Scaling),
                    (int)(SelectionRectangle.Height * _currentScreen.Scaling)];
                var image = childImage.ImageToBitmap();
                // image.dp = new Size(SelectionRectangle.Width, SelectionRectangle.Height);
                UIManager.ShowPreviewImageWindowAtMousePosition(image,
                    new Size(SelectionRectangle.Width, SelectionRectangle.Height));
            }
            catch (Exception e)
            {
                Log.Error(e.StackTrace);
            }
        }

        Close();
    }
}