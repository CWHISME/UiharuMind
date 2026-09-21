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
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Shared.Windows;

public interface IDockedWindow
{
    public event Action? OnPreCloseEvent;

    /// <summary>
    /// 停靠锚点变了（位置或内容尺寸）。
    /// <see cref="Window.PositionChanged"/> 在系统拖动循环里太稀疏、缩放时又比内容尺寸早一步到，
    /// 所以由目标窗在自己确实知道锚点变了的时候补一次通知。
    /// </summary>
    event Action? DockAnchorChanged;

    /// <summary>
    /// 停靠锚点的窗口原点（Position 口径）。
    /// 系统拖动期间 <see cref="Window.Position"/> 约 110ms 才回灌一次，跟随窗照它贴就是一卡一卡，
    /// 所以由目标窗给出「此刻真正在哪」。
    /// </summary>
    PixelPoint DockAnchorPosition { get; }

    /// <summary>
    /// 停靠锚点：内容区相对窗口左上的位置与大小（DIP）。
    /// 停靠窗贴的是内容边缘，而不是窗口边缘——窗口可能为阴影之类多留了一圈透明留白。
    /// </summary>
    Rect DockAnchorBounds { get; }
}

public class DockWindow<T> : UiharuWindowBase where T : Window, IDockedWindow
{
    protected T? CurrentSnapWindow;

    private bool _pendingReveal; //本次 RequestShow 是真的从隐藏到显示，出现动画留到窗口显示后再播
    private OpacityChannel? _revealOpacity;
    private bool _spanningScreens; //横跨两块屏期间先藏起来，躲开系统重新归属窗口时的花帧
    private bool _hiding; //淡出播放中，此时改贴别的窗口要把它截下来

    public override bool IsCacheWindow => true;
    public override bool ContributesToMacRegularMode => false;

    protected override bool IsAllowFocusOnOpen => false;

    public DockWindow()
    {
        this.SetSimpledecorationWindow();
        ShowActivated = false;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        MainWindow_OnMouseLeave(this, e);
    }

    public void SetMainWindow(T? mainWindow)
    {
        // Log.Debug($"SetMainWindow {mainWindow}");
        if (mainWindow is not { IsVisible: true })
        {
            _hiding = true;
            PlayReveal(false, () =>
            {
                _hiding = false;
                SafeClose();
            });
            return;
        }

        // 在两个贴图之间移动时，先收到旧窗的 PointerExited（已经开播淡出）、再收到新窗的 PointerEntered，
        // 不把淡出截下来它就会一路播完并关窗，看着是闪一下就没了。
        // 截的方式分两种：贴的还是同一个窗就从当前值淡回来（重播会弹一下），
        // 换了窗则要先落到新位置、再整段重播，否则等于没有动画
        bool wasOnScreen = IsVisible || _hiding;
        _hiding = false;

        // 凡是接下来要播出现动画的，都必须赶在 Show/挪位置之前先按到隐藏态，中间不留一帧满透明度的旧样子：
        // 换窗时是先挪到新贴图下方才置隐，隐藏出现时则是 Show() 会立刻把上次渲染的满透明度内容贴出来
        //（缓存窗的 Show 是 Post 的，置隐若留到 OnPostShow 就晚了一帧）。
        // 只有「同一个贴图且还在屏上」例外，那种情况是从当前值淡回来，不能置隐
        bool keepCurrentState = wasOnScreen && ReferenceEquals(mainWindow, CurrentSnapWindow);
        if (!keepCurrentState) UiAnimationUtils.PrepareVerticalRevealTarget(Content as Control, RevealOpacity);

        if (ReferenceEquals(mainWindow, CurrentSnapWindow))
        {
            // Log.Debug($"SetMainWindow {mainWindow} ReferenceEquals");
            if (wasOnScreen) PlayReveal(true, fromHidden: false);
            else _pendingReveal = true;
            RequestShow(isActivate: false);
            UpdateFollowerWindowPosition();
            return;
        }

        if (CurrentSnapWindow != null)
        {
            CurrentSnapWindow.PositionChanged -= MainWindow_PositionChanged;
            CurrentSnapWindow.DockAnchorChanged -= UpdateFollowerWindowPosition;
            CurrentSnapWindow.SizeChanged -= MainWindow_SizeChanged;
            CurrentSnapWindow.PointerExited -= MainWindow_OnMouseLeave;
            CurrentSnapWindow.Closing -= MainWindow_OnClose;
            CurrentSnapWindow.OnPreCloseEvent -= MainWindow_OnClose;
        }

        // Log.Debug($"SetMainWindow {mainWindow} set");

        CurrentSnapWindow = mainWindow;

        CurrentSnapWindow.PositionChanged += MainWindow_PositionChanged;
        CurrentSnapWindow.DockAnchorChanged += UpdateFollowerWindowPosition;
        CurrentSnapWindow.SizeChanged += MainWindow_SizeChanged;
        CurrentSnapWindow.PointerExited += MainWindow_OnMouseLeave;
        CurrentSnapWindow.Closing += MainWindow_OnClose;
        CurrentSnapWindow.OnPreCloseEvent += MainWindow_OnClose;

        RequestShow(isActivate: false);
        UpdateFollowerWindowPosition();
        // 落位之后再播，动画才发生在新贴图那儿；还没显示则留给 OnPostShow
        if (wasOnScreen) PlayReveal(true);
        else _pendingReveal = true;
        // Log.Debug($"SetMainWindow {mainWindow} UpdateFollowerWindowPosition");
    }

    // 缓存窗的 Show 是 Post 到空闲队列的，出现动画只能等窗口真显示了再播，
    // 否则透明度在窗口露面之前就跑完了，看着只剩位移
    protected override void OnPostShow()
    {
        base.OnPostShow();
        if (!_pendingReveal) return;
        _pendingReveal = false;
        PlayReveal(true);
    }

    // 贴着目标窗出现/消失，硬切太生硬；隐藏播完才真正关窗，
    // 半途又要显示时前一个动画被取消，关窗回调也随之作废（见 UiAnimationUtils）
    private void PlayReveal(bool isShow, Action? onCompleted = null, bool fromHidden = true)
    {
        UiAnimationUtils.PlayVerticalRevealAnimation(Content as Control, isShow, onCompleted, fromHidden,
            RevealOpacity);
    }

    // 透明度走原生窗口 alpha：托管 Opacity 要等下一次渲染，而挪窗口/显示窗口是原生立即生效的，
    // 那一帧合成器会把上一帧渲染好的满透明度内容直接贴到新位置——就是切换贴图时闪的那一下。
    // 通道只记显隐动画要的值，真正落下去的还要乘上跨屏这一档，见 ApplyWindowAlpha
    private OpacityChannel RevealOpacity => _revealOpacity ??= new OpacityChannel(_ => ApplyWindowAlpha());

    // 拿不到原生通道（非 macOS）就回退托管 Opacity——那条路慢一帧，跨屏遮挡的效果也就打折
    private void ApplyWindowAlpha()
    {
        double alpha = _spanningScreens ? 0 : RevealOpacity.Value;
        if (OverlayWindowService.TrySetNativeWindowAlpha(this, alpha)) return;
        if (Content is Control content) content.Opacity = alpha;
    }

    // 贴图被拖过屏幕交界时，跟随窗会被我们高频改位置，横跨两块屏的那段时间里系统要反复
    // 重新归属并重绘它，中间的帧是花的。这段时间直接藏掉：原生 alpha 立即生效，不占渲染
    private void UpdateScreenSpanState()
    {
        double scaling = App.ScreensService.Scaling;
        var size = new PixelSize((int)Math.Round(Width * scaling), (int)Math.Round(Height * scaling));
        bool spanning = size.Width > 0 && size.Height > 0 &&
                        !App.ScreensService.IsWithinSingleScreen(new PixelRect(Position, size));
        if (spanning == _spanningScreens) return;

        _spanningScreens = spanning;
        ApplyWindowAlpha();
    }

    private void MainWindow_OnClose()
    {
        SetMainWindow(null);
    }

    private void MainWindow_OnClose(object? sender, WindowClosingEventArgs e)
    {
        SetMainWindow(null);
    }

    private void MainWindow_PositionChanged(object? sender, PixelPointEventArgs e)
    {
        UpdateFollowerWindowPosition();
    }

    private void MainWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateFollowerWindowPosition();
    }

    private void MainWindow_OnMouseLeave(object? sender, PointerEventArgs e)
    {
        // Log.Debug($"MainWindow_OnMouseLeave {sender} {e}");
        if (!CheckInValidBounds()) SetMainWindow(null);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        UpdateFollowerWindowPosition();
    }

    /// <summary>
    /// 是否处于合适区域
    /// </summary>
    /// <returns></returns>
    private bool CheckInValidBounds()
    {
        if (CurrentSnapWindow == null) return false;

        var mousePos = App.ScreensService.MousePosition;
        var scaling = App.ScreensService.Scaling;
        //检测是否处于组合区域内
        double offset = 10f * scaling;
        var selfWindowBounds = new Rect(this.Position.X + offset, this.Position.Y + offset,
            this.Width * scaling - offset, this.Height * scaling - offset);
        var targetWindowBounds = new Rect(CurrentSnapWindow.Position.X + offset, CurrentSnapWindow.Position.Y + offset,
            CurrentSnapWindow.Width * scaling - offset, CurrentSnapWindow.Height * scaling - offset);
        // 计算组合区域，包括两个窗口之间的间距
        var combinedBounds = selfWindowBounds.Union(targetWindowBounds);
        if (!combinedBounds.Contains(new Point(mousePos.X, mousePos.Y))) return false;

        // Log.Debug(
        //     $" mousePos:{mousePos} targetWindowBounds:{targetWindowBounds.Right}  selfWindowBounds：{selfWindowBounds.Right}");

        // 根据高度决定检测 mainWindowBounds 还是 bottomWindowBounds 的宽度
        //注：靠下才这样额外检测
        if (mousePos.Y < (targetWindowBounds.Bottom - offset) && mousePos.X < (targetWindowBounds.Right - offset))
        {
            // 位于目标(上方)窗口高度内，且处于其宽度内
            return true;
        }

        //位于底部窗口高度内，且处于其宽度内
        if (mousePos.Y < (selfWindowBounds.Bottom - offset) && mousePos.X < (selfWindowBounds.Right - offset))
        {
            return true;
        }

        return false;
    }

    private void UpdateFollowerWindowPosition()
    {
        // Log.Debug($"UpdateFollowerWindowPosition {CurrentSnapWindow} is now {CurrentSnapWindow?.GetType()}");
        if (!IsVisible || CurrentSnapWindow == null) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (CurrentSnapWindow == null)
                return;

            OnFollowTarget(CurrentSnapWindow.DockAnchorPosition, CurrentSnapWindow.DockAnchorBounds);
        });
    }

    /// <summary>
    /// 把自己摆到目标窗内容区的正下方。
    /// </summary>
    /// <param name="targetPosition">目标窗口位置（Position 口径）</param>
    /// <param name="anchor">目标窗的内容区（相对其左上，DIP）</param>
    protected virtual void OnFollowTarget(PixelPoint targetPosition, Rect anchor)
    {
        var scaling = App.ScreensService.Scaling;
        Position = new PixelPoint(
            targetPosition.X + (int)(anchor.X * scaling),
            targetPosition.Y + (int)(anchor.Bottom * scaling) + 2);
        UpdateScreenSpanState();
    }
}
