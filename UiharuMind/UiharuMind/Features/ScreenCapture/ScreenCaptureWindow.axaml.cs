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
using Avalonia.Threading;
using HPPH;
using UiharuMind.Resources.Lang;
using UiharuMind.Features.ScreenCapture.Frames;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;
using UiharuMind.Shared.Windows;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.UiharuScreenCapture;

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

    // 显示前只备帧不落几何，OnPostShow 再落位（见 UpdateCaptureScreen）
    private bool _deferredGeometry;

    // 窗口收不到的按下（菜单栏顶边等）由全局钩子补位，组合而非继承
    private GlobalPointerDriver? _hookDriver;

    // private bool _error = false;

    public override bool IsCacheWindow => false;
    public override bool ContributesToMacRegularMode => false;

    public ScreenCaptureWindow()
    {
        InitializeComponent();
        InitializeWindow();
        _hookDriver = new GlobalPointerDriver(this, () => _currentScreen);
        _hookDriver.Pressed += OnHookPressed;
        _hookDriver.Moved += OnHookMoved;
        _hookDriver.Released += OnHookReleased;
        _hookDriver.RightPressed += OnHookRightPressed;

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
        // 先隐身示人：首帧布局与位图上传完成前，合成器看到全透明窗口就是一闪
        Opacity = 0;
        this.SetSimpledecorationPureWindow(true);
    }

    protected override void OnPreShow()
    {
        UpdateCaptureScreen();
    }

    protected override void OnPostShow()
    {
        base.OnPostShow();
        // 分步隔离：任何一步炸了都不能把窗晾在半初始化状态（隐形全屏窗会吃掉所有点击）
        try
        {
            // Show 之后再抬层级：与 capcap 同方案，盖住菜单栏/Dock
            OverlayWindowService.ApplyNativeFullscreenOverlayStyle(this);
        }
        catch (Exception e)
        {
            Log.Warning($"遮罩抬层级失败：{e.Message}");
        }

        try
        {
            // 收起本应用菜单：菜单标题会拦截点击，空菜单栏区域才能落到遮罩上
            OverlayWindowService.SuppressAppMenuForCapture();
        }
        catch (Exception e)
        {
            Log.Warning($"收起应用菜单失败：{e.Message}");
        }

        try
        {
            if (_deferredGeometry)
            {
                _deferredGeometry = false;
                ApplyGeometryAndShow();
            }
        }
        catch (Exception e)
        {
            Log.Warning($"遮罩落几何失败：{e.Message}");
        }

        // 最终兜底：上面无论哪步出岔子，1 秒后只要窗还在就强制显形，
        // 绝不留一扇隐形全屏窗在顶层吃点击
        DispatcherTimer.RunOnce(() =>
        {
            if (IsVisible && Opacity < 1.0)
            {
                Log.Warning("截图遮罩显形兜底触发，可能有步骤失败，检查上方日志。");
                Opacity = 1.0;
            }
        }, TimeSpan.FromMilliseconds(1000));
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
        OverlayWindowService.RestoreAppMenuAfterCapture();
        StopRevealTimer();
        _hookDriver?.Dispose();
        _hookDriver = null;
        ClearData();
    }

    // 钩子补位事件：窗口事件坐标精确，到了会覆盖这里的值（同一次物理事件，幂等）
    private void OnHookPressed(Point windowDip, PixelPoint screenUnits)
    {
        if (!MainPanel.IsVisible) return;
        _lastPointerPixel = screenUnits;
        RevealTipsOnFirstPointer();
        BeginSelection(windowDip);
    }

    private void OnHookMoved(Point windowDip, PixelPoint screenUnits)
    {
        if (!_isSelecting) return;
        _lastPointerPixel = screenUnits;
        UpdateSelectionRect(windowDip);
    }

    private void OnHookReleased(PixelPoint screenUnits)
    {
        if (!_isSelecting) return;
        _releasedPointerPixel = screenUnits;
        DoAreaCapture();
    }

    private void OnHookRightPressed()
    {
        SafeClose(0.15f);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!MainPanel.IsVisible) return;
        TrackPointer(e);
        RevealTipsOnFirstPointer();
        PointerUpdateKind pointerUpdateKind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        // 右键按下分两种：纯右键是取消；带 Alt（不带 Control/Command）的右键按框选处理。
        // 理由有二：macOS 把 Control+左键报成右键；触发快捷键默认 Alt+Shift+Z，
        // 手指没松开就拖时，这次点击带着 Alt 余键进来，或被触控板认成双指次级点按。
        // 此时用户本意都是框选。真想取消的话，纯右键与 Esc 都还在
        bool hasAlt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        bool hasControl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool hasMeta = e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        bool isControlLeft = pointerUpdateKind == PointerUpdateKind.RightButtonPressed && hasControl;
        bool isAltRightAsLeft = pointerUpdateKind == PointerUpdateKind.RightButtonPressed &&
                                hasAlt && !hasControl && !hasMeta;
        if (pointerUpdateKind == PointerUpdateKind.LeftButtonPressed || isControlLeft || isAltRightAsLeft)
        {
            BeginSelection(e.GetPosition(ScreenshotCanvas));
        }
        else if (pointerUpdateKind == PointerUpdateKind.RightButtonPressed)
        {
            SafeClose(0.15f);
        }
    }

    // 以窗内 DIP 坐标开始一次框选。窗口事件与钩子事件都会调它：
    // 窗口事件坐标精确，总是覆盖；钩子只补窗口收不到的那一下（菜单栏顶边）
    private void BeginSelection(Point windowDip)
    {
        _isSelecting = true;
        _startPoint = windowDip;
        SelectionRectangle.Width = 0;
        SelectionRectangle.Height = 0;
        // InfoPanel.IsVisible = true;
        Canvas.SetLeft(SelectionRectangle, windowDip.X);
        Canvas.SetTop(SelectionRectangle, windowDip.Y);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!MainPanel.IsVisible) return;
        TrackPointer(e);
        RevealTipsOnFirstPointer();
        if (!_isSelecting)
        {
            UpdateExtraInfo();
            return;
        }

        if (_currentScreen == null) return;

        UpdateCaptureScreen();

        UpdateSelectionRect(e.GetPosition(ScreenshotCanvas));
    }

    // 按窗内 DIP 坐标刷新选区框。窗口移动与钩子移动都会调它，同一手势内坐标一致，幂等
    private void UpdateSelectionRect(Point currentPosition)
    {
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
        // 四舍五入而非截断：右下角最后一个像素中心 1919.5 应记为 1920，否则 tips 与选区永远少 1px
        var local = e.GetPosition(this);
        double scaling = _currentScreen.Scaling;
        var offset = new PixelVector((int)Math.Round(local.X * scaling), (int)Math.Round(local.Y * scaling));
        _lastPointerPixel = _currentScreen.Bounds.Position + offset;
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

            // 物理像素：换算收敛到 DisplayUnits（mac 的 Bounds/选区是 point，Windows 下本来就是像素）
            double boundsToPixels = DisplayUnits.ScreenBoundsToPixels(_currentScreen.Scaling, RenderScaling);
            double pixelsPerDip = DisplayUnits.PixelsPerDip(_currentScreen.Scaling, RenderScaling);
            if (correct)
            {
                width = (int)Math.Ceiling(width * pixelsPerDip);
                height = (int)Math.Ceiling(height * pixelsPerDip);
            }
            else
            {
                width = (int)Math.Ceiling(width * boundsToPixels);
                height = (int)Math.Ceiling(height * boundsToPixels);
            }

            // PixelPoint pixelPoint = PixelPoint.FromPoint(point, _currentScreen.Scaling);
            var mousePosition = new PixelPoint(
                (int)Math.Round(_lastPointerPixel.X * boundsToPixels),
                (int)Math.Round(_lastPointerPixel.Y * boundsToPixels));
            PositionText.Text =
                $"{Lang.ScreenCapturePosition}:({Math.Clamp(mousePosition.X, 0, (int)(_currentScreen.Bounds.Width * boundsToPixels))},{Math.Clamp(mousePosition.Y, 0, (int)(_currentScreen.Bounds.Height * boundsToPixels))})";
            ResolutionText.Text = $"{Lang.ScreenCaptureResolution}:({width}x{height})";
            // TipsText.Text = $"{point.X} {point.Y}";
            Point point = position.ToPoint(_currentScreen.Scaling);
            // Log.Debug($"position:({position.X},{position.Y}) point:({point.X},{point.Y})");
            // Margin 是窗内相对坐标，屏幕坐标要先减掉窗口原点（主屏原点为 0 才一直没暴露）
            Point origin = Position.ToPoint(_currentScreen.Scaling);
            InfoPanel.Margin = new Thickness(Math.Floor(point.X - origin.X), Math.Floor(point.Y - origin.Y), 0, 0);
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
        // 预抓帧是外部交入的所有权，ClearData 会连它一起释放——先取出再交还，
        // 让 CaptureScreen 按原逻辑消费
        IScreenFrame? preCaptured = _pendingFrame;
        _pendingFrame = null;
        ClearData();
        if (preCaptured != null)
        {
            _pendingFrame = preCaptured;
            _pendingScreen = currentScreen;
        }
        // Log.Debug("更新截图");
        //截屏
        await CaptureScreen();
        // Log.Debug("截图完成");
        //更新截图数据
        _currentScreen = currentScreen;
        if (!IsVisible)
        {
            // 显示前只备好帧：此时设尺寸会被 native 按 visibleFrame 裁掉，
            // 遮罩一生下来就小于全屏。几何等 OnPostShow 一次落位
            _deferredGeometry = true;
            return;
        }

        ApplyGeometryAndShow();
    }

    // Show 之后落几何：native 不再裁剪，位置与尺寸经原子提交一次落盘
    private void ApplyGeometryAndShow()
    {
        if (_currentScreen == null || _frame == null) return;
        var bounds = _currentScreen.Bounds;
        var scaling = _currentScreen.Scaling;
        var size = new Size(bounds.Width / scaling, bounds.Height / scaling);
        var pos = bounds.Position;
        if (this.TrySetWindowFrame(pos, size))
        {
            // 原子提交已落位，只同步托管尺寸供内容布局
            Width = size.Width;
            Height = size.Height;
        }
        else
        {
            Position = pos;
            Width = size.Width;
            Height = size.Height;
        }

        //展示截图
        DisplayCapture();

        // 首秀才需要等几何落位：跨屏重抓时窗口本来就是可见的，不能再藏
        if (Opacity < 1.0)
            ScheduleReveal(size);
    }

    private DispatcherTimer? _revealTimer;
    private int _revealSteadyFrames;
    private int _revealTicks;

    // 轮询等原生 frame 真正长到目标尺寸：布局→ClientSize→setContentSize 是异步链，
    // 定时猜（比如 50ms）极易在半路提前打开，看到的就是从小撑大加横向撕裂。
    // ClientSize到位即布局已出，原生调用是同步跟下来的，再稳两帧给合成器呈现，必不闪；
    // 30 拍（约半秒）还没好就直接放行，不能一直藏着
    private void ScheduleReveal(Size target)
    {
        StopRevealTimer();
        _revealSteadyFrames = 0;
        _revealTicks = 0;
        _revealTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _revealTimer.Tick += (_, _) =>
        {
            _revealTicks++;
            if (_frame == null || _currentScreen == null)
            {
                StopRevealTimer();
                return;
            }

            if (Math.Abs(ClientSize.Width - target.Width) < 1.0 &&
                Math.Abs(ClientSize.Height - target.Height) < 1.0)
                _revealSteadyFrames++;
            else
                _revealSteadyFrames = 0;

            if (_revealSteadyFrames >= 2 || _revealTicks >= 30)
            {
                Opacity = 1.0;
                StopRevealTimer();
            }
        };
        _revealTimer.Start();
    }

    private void StopRevealTimer()
    {
        if (_revealTimer == null) return;
        _revealTimer.Stop();
        _revealTimer = null;
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
        // InfoPanel 等第一次拿到鼠标位置再显示：刚打开时 _lastPointerPixel 还是 (0,0)，
        // 直接显示会在左上角闪一下。见 OnPointerMoved/OnPointerPressed。
        UpdateExtraInfo();
    }

    // 第一次拿到可信鼠标位置时再把 tips 显示出来
    private void RevealTipsOnFirstPointer()
    {
        if (!InfoPanel.IsVisible) InfoPanel.IsVisible = true;
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
