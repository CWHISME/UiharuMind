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
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using UiharuMind.Features.ScreenCapture.Frames;
using UiharuMind.Features.ScreenCapture.Overlay;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;
using UiharuMind.Shared.Windows;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core;

namespace UiharuMind.Features.ScreenCapture;

/// <summary>
/// 全屏选区遮罩窗：铺满目标屏显示一帧冻结画面，用户在上面框选，松手即出图。
/// 本类只做三件事——生命周期、输入分发、冻结帧的所有权；
/// 几何显形、选区暗层、放大镜取色、信息面板各自拆在 <c>Overlay</c> 下，见各自的类注释。
/// </summary>
public partial class ScreenCaptureWindow : UiharuWindowBase
{
    private readonly CaptureOverlayGeometry _geometry;
    private readonly CaptureSelectionLayer _selection;
    private readonly CaptureMagnifier _magnifier;
    private readonly CaptureInfoPanel _infoPanel;

    private Screen? _currentScreen;

    //当前屏的冻结画面(底图+裁剪能力),几 MB 到几十 MB,本窗唯一所有者
    private IScreenFrame? _frame;

    //Linux 下必须在遮罩窗显示之前抓图,否则 Portal 抓到的是遮罩自己;抓好的帧经此字段交进来
    private IScreenFrame? _pendingFrame;

    //预抓帧所属的屏幕。非空即表示本次截图走预抓路径，不再跟随鼠标切屏
    private Screen? _pendingScreen;

    //遮罩窗铺满整屏并独占指针,窗内事件坐标就是屏幕坐标真值。
    //不再向全局钩子要鼠标位置:纯 Wayland 下拿不到,而这里本来就不需要
    private PixelPoint _lastPointerPixel;
    private PixelPoint _releasedPointerPixel;

    // 显示前只备帧不落几何，OnPostShow 再落位（见 UpdateCaptureScreen）
    private bool _deferredGeometry;

    // 窗口收不到的按下（菜单栏顶边等）由全局钩子补位，组合而非继承
    private GlobalPointerDriver? _hookDriver;

    // 本次框选是不是钩子起的头。窗口自己起的头就一路只认窗口事件：
    // 遮罩铺满全屏且独占指针，后续移动窗口一定收得到，钩子再掺一脚只会两个来源互相顶
    private bool _selectionFromHook;

    public override bool IsCacheWindow => false;
    public override bool ContributesToMacRegularMode => false;

    public ScreenCaptureWindow()
    {
        InitializeComponent();

        _geometry = new CaptureOverlayGeometry(this);
        _selection = new CaptureSelectionLayer(SelectionRectangle, DimTop, DimBottom, DimLeft, DimRight,
            () => new Size(Width, Height));
        _magnifier = new CaptureMagnifier(MagnifierImage, MagnifierGridLines, MagnifierCross, MagnifierSwatch,
            MagnifierPositionText, MagnifierColorText, MagnifierHintCopy);
        _infoPanel = new CaptureInfoPanel(InfoPanel, MagnifierPanel, SelectionInfoPanel, PositionText, ResolutionText);

        InitializeWindow();

        _hookDriver = new GlobalPointerDriver(this, () => _currentScreen);
        _hookDriver.Pressed += OnHookPressed;
        _hookDriver.Moved += OnHookMoved;
        _hookDriver.Released += OnHookReleased;
        _hookDriver.RightPressed += OnHookRightPressed;
    }

    private void InitializeWindow()
    {
        CanResize = false;
        ShowInTaskbar = false;
        // 先隐身示人：首帧布局与位图上传完成前，合成器看到全透明窗口就是一闪
        Opacity = 0;
        this.SetSimpledecorationPureWindow(true);
    }

    #region 生命周期

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
            MacAppMenuService.SuppressAppMenuForCapture();
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

    protected override void OnPreClose()
    {
        _currentScreen = null;
        MacAppMenuService.RestoreAppMenuAfterCapture();
        _geometry.Stop();
        _hookDriver?.Dispose();
        _hookDriver = null;
        ClearData();
    }

    #endregion

    #region 输入

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.LeftShift || e.Key == Key.RightShift)
        {
            _magnifier.ToggleColorFormat();
            e.Handled = true;
            return;
        }

        bool copyModifier = e.KeyModifiers.HasFlag(
            UiharuCoreManager.Instance.IsMacOs ? KeyModifiers.Meta : KeyModifiers.Control);
        if (e.Key == Key.C && copyModifier)
        {
            _magnifier.CopyColor(TopLevel.GetTopLevel(this)?.Clipboard);
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Escape) Close();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!MainPanel.IsVisible) return;
        TrackPointer(e);
        _infoPanel.Reveal();
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
            _selectionFromHook = false;
            BeginSelection(e.GetPosition(ScreenshotCanvas));
        }
        else if (pointerUpdateKind == PointerUpdateKind.RightButtonPressed)
        {
            SafeClose(0.15f);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!MainPanel.IsVisible) return;
        // 窗口收得到移动，这一次拖动就归窗口管（钩子坐标与窗口事件差着舍入，两边交替刷新会抖）
        _selectionFromHook = false;
        TrackPointer(e);
        _infoPanel.Reveal();
        if (!_selection.IsSelecting)
        {
            UpdateMagnifier();
            return;
        }

        if (_currentScreen == null) return;

        UpdateCaptureScreen();
        UpdateSelection(e.GetPosition(ScreenshotCanvas));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        TrackPointer(e);
        _releasedPointerPixel = _lastPointerPixel;
        if (_selection.IsSelecting) DoAreaCapture();
    }

    // 钩子补位事件：窗口事件坐标精确，到了会覆盖这里的值（同一次物理事件，幂等）
    private void OnHookPressed(Point windowDip, PixelPoint screenUnits)
    {
        if (!MainPanel.IsVisible || _selection.IsSelecting) return;
        _selectionFromHook = true;
        _lastPointerPixel = screenUnits;
        _infoPanel.Reveal();
        BeginSelection(windowDip);
    }

    private void OnHookMoved(Point windowDip, PixelPoint screenUnits)
    {
        if (!_selectionFromHook || !_selection.IsSelecting) return;
        _lastPointerPixel = screenUnits;
        UpdateSelection(windowDip);
    }

    // 松手不分来源：窗口自己收到的那一下已经把 IsSelecting 置回 false，这里再进来是空跑。
    // 反过来窗口漏掉时（拖到别的屏上松手），只有这里能把这次框选收尾
    private void OnHookReleased(PixelPoint screenUnits)
    {
        if (!_selection.IsSelecting) return;
        _releasedPointerPixel = screenUnits;
        DoAreaCapture();
    }

    private void OnHookRightPressed()
    {
        SafeClose(0.15f);
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

    #endregion

    #region 选区与放大镜

    private void BeginSelection(Point windowDip)
    {
        _selection.Begin(windowDip);
        _infoPanel.ShowSelectionPage();
    }

    private void UpdateSelection(Point windowDip)
    {
        _selection.Update(windowDip);
        if (_currentScreen == null) return;
        _infoPanel.ShowSelectionSize(_currentScreen, RenderScaling, _lastPointerPixel, Position,
            _selection.Selection.Size);
    }

    private void UpdateMagnifier()
    {
        if (_frame == null || _currentScreen == null) return;
        _infoPanel.ShowMagnifierPage();
        _magnifier.Update(_frame, _currentScreen, _lastPointerPixel, RenderScaling);
        _infoPanel.FollowPointer(_currentScreen, _lastPointerPixel, Position);
    }

    /// <summary>
    /// 执行区域截图，完毕后关闭界面，并弹出预览窗口
    /// </summary>
    private void DoAreaCapture()
    {
        _selection.Finish();
        var selection = _selection.Selection;
        if (_frame == null || _currentScreen == null || selection.Width <= 0 || selection.Height <= 0)
        {
            Close();
            return;
        }

        // 起点与终点都来自本窗的指针事件，换算到屏幕坐标后交给帧裁剪
        var scaling = _currentScreen.Scaling;
        var origin = _currentScreen.Bounds.Position;
        var startPixelPoint = origin + (PixelVector)PixelPoint.FromPoint(_selection.StartPoint, scaling);
        var endPixelPoint = _releasedPointerPixel;
        var region = new PixelRect(
            Math.Min(startPixelPoint.X, endPixelPoint.X),
            Math.Min(startPixelPoint.Y, endPixelPoint.Y),
            (int)(selection.Width * scaling),
            (int)(selection.Height * scaling));

        try
        {
            var image = _frame.Crop(region);
            if (image != null)
            {
                // 截图即复制:剪贴板那份必须是独立的一张(见 ClipboardService 注释),预览窗接管原图
                Bitmap? forClipboard = image.CloneBitmap();
                if (forClipboard != null) App.Clipboard.CopyImageToClipboard(forClipboard, true);
                RecordToHistoryInBackground(image.CloneBitmap());
                // 裁出来的是不带 DPI 的物理像素图，显示尺寸由抓图那一侧的屏幕换算给出：
                // 让预览窗自己猜的话，它可能开在另一块缩放不同的屏上（mac 尤甚，Scaling 恒为 1）
                var displaySize = image.PixelSize.ToSize(DisplayUnits.PixelsPerDip(scaling, RenderScaling));
                //校正截图的上下左右不同方向拖动方式
                UIManager.ShowPreviewImageWindowAtMousePosition(image, startPixelPoint, endPixelPoint, displaySize);
            }
        }
        catch (Exception e)
        {
            Log.Warning(e.StackTrace);
        }

        Close();
    }

    /// 落盘要做一次 PNG 编码，整屏实测 1 秒上下，占着 UI 线程预览窗就得干等这么久。
    /// 丢后台跑，并且必须给它一份独立副本：原图已经归预览窗，随时可能被释放。
    /// ClipboardHistoryStore 自带锁，启动时的补记本来就是后台线程在写
    private static void RecordToHistoryInBackground(Bitmap? image)
    {
        if (image == null) return;
        Task.Run(() =>
        {
            try
            {
                App.Clipboard.RecordImageToHistory(image);
            }
            catch (Exception e)
            {
                Log.Warning($"截图落盘失败：{e.Message}");
            }
            finally
            {
                image.Dispose();
            }
        });
    }

    #endregion

    #region 冻结帧与几何

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
        // 预抓帧只有一张：遮罩可见后重抓会把自己也抓进底图，因此不跟随切屏
        if (_pendingScreen != null && _frame != null) return;

        var currentScreen = _pendingScreen ?? App.ScreensService.MouseScreen;
        if (currentScreen == _currentScreen || currentScreen == null) return;

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

        await CaptureScreen();
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
        var size = _geometry.ApplyTo(_currentScreen);

        DisplayCapture();

        // 首秀才需要等几何落位：跨屏重抓时窗口本来就是可见的，不能再藏
        if (Opacity < 1.0)
            _geometry.ScheduleReveal(size, () => _frame != null && _currentScreen != null, () => Opacity = 1.0);
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

    private void DisplayCapture()
    {
        MainPanel.IsVisible = true;
        // InfoPanel 等第一次拿到鼠标位置再显示：刚打开时 _lastPointerPixel 还是 (0,0)，
        // 直接显示会在左上角闪一下。见 OnPointerMoved/OnPointerPressed。
        _infoPanel.ShowMagnifierPage();
        _selection.RefreshDim();
        if (_currentScreen != null)
            _infoPanel.ShowScreenSize(_currentScreen, RenderScaling, _lastPointerPixel, Position);
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

    private void ClearData()
    {
        _selection.Reset();
        _infoPanel.Hide();
        MainPanel.IsVisible = false;
        // 放大镜借了底图的视图，先断开再释放帧，否则渲染线程撞上已释放的位图
        _magnifier.Detach();
        SetFrame(null);
        _pendingFrame?.Dispose();
        _pendingFrame = null;
        _pendingScreen = null;
        _lastPointerPixel = default;
        _releasedPointerPixel = default;
        _selectionFromHook = false;
        _currentScreen = null;
    }

    #endregion
}
