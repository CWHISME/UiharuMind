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
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HPPH;
using UiharuMind.Resources.Lang;
using UiharuMind.Features.ScreenCapture.Frames;
using UiharuMind.Shared.Utils;
using UiharuMind.Shared.Windows;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.UiharuScreenCapture;
using UiharuMind.Core.Input;
using UiharuMind.Core;

namespace UiharuMind.Features.ScreenCapture;

public partial class ScreenCaptureWindow : UiharuWindowBase
{
    private Point _startPoint;

    private bool _isSelecting;

    // private int _screenWidth;
    // private int _screenHeight;
    private Screen? _currentScreen;
    //当前屏的冻结画面(底图+裁剪能力),几 MB 到几十 MB,本窗唯一所有者
    private IScreenFrame? _frame;

    //Linux 下必须在遮罩窗显示之前抓图,否则 Portal 抓到的是遮罩自己;抓好的帧经此字段交进来
    private IScreenFrame? _pendingFrame;

    //遮罩窗铺满整屏并独占指针,窗内事件坐标就是屏幕坐标真值。
    //不再向全局钩子要鼠标位置:纯 Wayland 下拿不到,而这里本来就不需要
    private PixelPoint _lastPointerPixel;
    private PixelPoint _releasedPointerPixel;

    //预抓帧所属的屏幕。非空即表示本次截图走预抓路径，不再跟随鼠标切屏
    private Screen? _pendingScreen;

    // private bool _error = false;

    public override bool IsCacheWindow => false;
    public override bool ContributesToMacRegularMode => false;

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
        CanResize = false;
        ShowInTaskbar = false;
        this.SetSimpledecorationPureWindow(true);
    }

    protected override void OnPreShow()
    {
        UpdateCaptureScreen();
    }

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
        if (!MainPanel.IsVisible) return;
        TrackPointer(e);
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
            SafeClose(0.15f);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!MainPanel.IsVisible) return;
        TrackPointer(e);
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
        TrackPointer(e);
        _releasedPointerPixel = _lastPointerPixel;
        if (_isSelecting) DoAreaCapture();
    }

    /// 把窗内事件坐标换算成桌面绝对像素并留存。遮罩窗铺满目标屏且独占指针，
    /// 这就是本次截图期间唯一可靠的鼠标位置来源，纯 Wayland 下同样成立
    private void TrackPointer(PointerEventArgs e)
    {
        if (_currentScreen == null) return;
        var local = PixelPoint.FromPoint(e.GetPosition(this), _currentScreen.Scaling);
        _lastPointerPixel = _currentScreen.Bounds.Position + (PixelVector)local;
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
            var position = UiUtils.EnsurePositionWithinScreen(_currentScreen, _lastPointerPixel,
                InfoPanel.Bounds.Size, new Size(25, 25));

            if (correct)
            {
                width = (int)Math.Ceiling(width * _currentScreen.Scaling);
                height = (int)Math.Ceiling(height * _currentScreen.Scaling);
            }

            // PixelPoint pixelPoint = PixelPoint.FromPoint(point, _currentScreen.Scaling);
            var mousePosition = _lastPointerPixel;
            PositionText.Text =
                $"{Lang.ScreenCapturePosition}:({Math.Clamp(mousePosition.X, 0, _currentScreen.Bounds.Width)},{Math.Clamp(mousePosition.Y, 0, _currentScreen.Bounds.Height)})";
            ResolutionText.Text = $"{Lang.ScreenCaptureResolution}:({width}x{height})";
            // TipsText.Text = $"{point.X} {point.Y}";
            Point point = position.ToPoint(_currentScreen.Scaling);
            // Log.Debug($"position:({position.X},{position.Y}) point:({point.X},{point.Y})");
            InfoPanel.Margin = new Thickness(Math.Floor(point.X), Math.Floor(point.Y), 0, 0);
        }
        catch (Exception e)
        {
            Log.Warning(e.StackTrace);
        }
    }

    /// <summary>
    /// 交进一帧预先抓好的整屏画面。<b>本窗接管该帧</b>
    /// </summary>
    /// <param name="frame">已抓好的整屏帧</param>
    /// <param name="screen">该帧所属的屏幕</param>
    public void SetPreCapturedFrame(IScreenFrame frame, Screen screen)
    {
        _pendingFrame?.Dispose();
        _pendingFrame = frame;
        _pendingScreen = screen;
    }

    /// <summary>
    /// 动态切换了多屏，更新截图区域
    /// </summary>
    private async void UpdateCaptureScreen()
    {
        // 预抓帧只有一张：Linux 下重抓意味着再走一次 Portal，会再弹一次授权框，因此不跟随切屏
        if (_pendingScreen != null && _frame != null) return;

        var currentScreen = _pendingScreen ?? App.ScreensService.MouseScreen;
        if (currentScreen == _currentScreen || currentScreen == null) return;
        //清理当前数据
        // Log.Debug("清理当前数据");
        ClearData();
        // Log.Debug("更新截图");
        //截屏
        await CaptureScreen();
        // Log.Debug("截图完成");
        //更新截图数据
        _currentScreen = currentScreen;
        var bounds = _currentScreen.Bounds;
        Position = bounds.Position;
        var scaling = _currentScreen.Scaling;
        Width = bounds.Width / scaling;
        Height = bounds.Height / scaling;
        // WindowState = WindowState.FullScreen;
        //展示截图
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
        SetFrame(null);
        _pendingFrame?.Dispose();
        _pendingFrame = null;
        _pendingScreen = null;
        _startPoint = new Point(0, 0);
        _lastPointerPixel = default;
        _releasedPointerPixel = default;
        _isSelecting = false;
        _currentScreen = null;
    }

    /// 换掉整屏帧并释放上一帧。整屏位图是全应用最大的一次分配,又每次截图都来一张,
    /// 交给 GC 意味着连开几次截图就能堆出几百 MB。先把新值挂上界面、再释放旧值:
    /// 反过来做的话已释放的位图还挂在 Image.Source 上,下一帧渲染就撞上去
    private void SetFrame(IScreenFrame? frame)
    {
        IScreenFrame? stale = _frame;
        if (ReferenceEquals(stale, frame)) return;

        _frame = frame;
        ScreenshotImage.Source = frame?.Display;
        stale?.Dispose();
    }

    private void DisplayCapture()
    {
        MainPanel.IsVisible = true;
        InfoPanel.IsVisible = true;
        UpdateExtraInfo();
    }

    /// <summary>
    /// 取得当前屏的整屏画面：优先用外部预抓好的一帧，否则现场抓取
    /// </summary>
    private async Task CaptureScreen()
    {
        SetFrame(null);

        if (_pendingFrame != null)
        {
            var pending = _pendingFrame;
            _pendingFrame = null;
            SetFrame(pending);
            return;
        }

        var screen = App.ScreensService.MouseScreen;
        var frame = await ScreenFrameProvider.CaptureAsync(screen, App.ScreensService.MouseScreenIndex, this);
        if (frame == null)
        {
            Close();
            return;
        }

        SetFrame(frame);
    }

    /// <summary>
    /// 执行区域截图，完毕后关闭界面，并弹出预览窗口
    /// </summary>
    private void DoAreaCapture()
    {
        _isSelecting = false;
        if (_frame != null && _currentScreen != null && SelectionRectangle.Width > 0 &&
            SelectionRectangle.Height > 0)
        {
            try
            {
                // 起点与终点都来自本窗的指针事件，换算到桌面绝对像素后交给帧裁剪
                var scaling = _currentScreen.Scaling;
                var origin = _currentScreen.Bounds.Position;
                PixelPoint startPixelPoint = origin + (PixelVector)PixelPoint.FromPoint(_startPoint, scaling);
                PixelPoint endPixelPoint = _releasedPointerPixel;

                var region = new PixelRect(
                    Math.Min(startPixelPoint.X, endPixelPoint.X),
                    Math.Min(startPixelPoint.Y, endPixelPoint.Y),
                    (int)(SelectionRectangle.Width * scaling),
                    (int)(SelectionRectangle.Height * scaling));

                var image = _frame.Crop(region);
                if (image != null)
                {
                    // 落盘只是借用,必须排在移交之前:下一句起这张图就归预览窗了,它随时可能被释放
                    App.Clipboard.RecordImageToHistory(image);
                    //校正截图的上下左右不同方向拖动方式
                    UIManager.ShowPreviewImageWindowAtMousePosition(image, startPixelPoint, endPixelPoint);
                }
            }
            catch (Exception e)
            {
                Log.Warning(e.StackTrace);
            }
        }

        Close();
    }
}
