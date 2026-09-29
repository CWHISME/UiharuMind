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
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using SharpHook.Data;
using UiharuMind.Resources.Lang;
using UiharuMind.Generated;
using UiharuMind.Shared.Controls;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;
using UiharuMind.Shared.Windows;
using UiharuMind.Core.AI.Character.PromptActions;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.QuickChat;

using UiharuMind.Shared.WindowManagement;
namespace UiharuMind.Features.QuickTools;

/// <summary>
/// 当复制操作发生后，显示在复制位置的工具
/// </summary>
public partial class QuickToolWindow : QuickFloatingWindowBase
{
    public static void Show(string answerString)
    {
        UIManager.ShowWindow<QuickToolWindow>(x => x.SetAnswerString(answerString));
    }

    public QuickToolWindow()
    {
        InitializeComponent();
        LocalizationManager.Instance.LanguageChanged += InitFunctionMenu;
        InitFunctionMenu();
    }

    private string? _answerString;

    private OpacityChannel? _popupOpacity;
    private OpacityChannel PopupOpacity => _popupOpacity ??= new OpacityChannel(_ => ApplyWindowAlpha());

    public void SetAnswerString(string text)
    {
        _answerString = text;
    }

    /// <summary>
    /// 悬停期间是否保持窗口原生尺寸不变。macOS 上 Avalonia 在窗口尺寸变化后偶尔不再呈现新帧
    /// （画面停在被拉伸的旧帧或动画中途，托管布局和点击区域却都是对的，要等下一次尺寸变化才恢复），
    /// 所以窗口一开始就是展开宽度，展开/收起只动卡片；透明的多出部分靠 <see cref="CardHoverMask"/> 让点击穿透。
    /// </summary>
    protected virtual bool UsesFixedWindowWidth => OperatingSystem.IsMacOS();

    // 固定宽度窗口的初始宽度，足够放下各语言的菜单；实际展开更宽时再单向加宽
    private const double FixedWindowWidth = 480;

    private CardHoverMask? _hoverMask;

    public override void Awake()
    {
        base.Awake();
        if (!UsesFixedWindowWidth) return;
        SizeToContent = SizeToContent.Height;
        SetManagedWindowWidth(FixedWindowWidth);
    }

    protected override void OnPreShow()
    {
        base.OnPreShow();
        if (UsesFixedWindowWidth) ResetMenuState();
    }

    // 窗口是缓存复用的：上次可能带着展开的菜单关闭，再弹出必须回到收起态
    private void ResetMenuState()
    {
        _menuSlideCts?.Cancel();
        _menuIsShown = false;
        MainMenu.IsVisible = false;
        MainMenu.IsHitTestVisible = false;
        if (MainMenu.RenderTransform is TranslateTransform transform) transform.X = MenuSlideHiddenOffset;
        Card.ClearValue(WidthProperty);
        _cardWidth = double.NaN;
    }

    protected override void SetWindowPosition()
    {
        if (!UsesFixedWindowWidth)
        {
            base.SetWindowPosition();
            return;
        }

        // 按收起时的卡片宽度定位：窗口是固定宽度的，用它的宽度会让靠近屏幕右缘时整个圆按钮被无谓地左推
        this.SetWindowToMousePosition(HorizontalAlignment.Right, width: MeasureCardWidth(withMenu: false),
            offsetX: 15, offsetY: -15);
    }

    protected override void OnPostShow()
    {
        base.OnPostShow();
        // 无头/未初始化环境（App.ScreensService 未建）没有全局鼠标可跟踪，跳过
        if (UsesFixedWindowWidth && App.ScreensService != null)
        {
            // 位置在 Loaded 才落定，跟踪排在它之后开始；开始前窗口先穿透，别在错误位置上挡点击
            OverlayWindowService.TrySetNativeMouseEventsIgnored(this, true);
            _hoverMask ??= new CardHoverMask(this, () => new Size(Card.Bounds.Width, Card.Bounds.Height),
                inside => PlayAnimation(inside));
            Dispatcher.UIThread.Post(_hoverMask.Start, DispatcherPriority.Background);
        }

        // 弹出动画：淡入 + 轻微上浮，把「出现了」讲明白（macOS 走原生 alpha，防闪一帧，见 OverlayWindowService）
        if (Content is Control content)
        {
            UiAnimationUtils.PrepareVerticalRevealTarget(content, PopupOpacity);
            UiAnimationUtils.PlayVerticalRevealAnimation(content, true, opacityChannel: PopupOpacity);
        }
    }

    // 拿不到原生通道（非 macOS）就回退托管 Opacity——那条路慢一帧，弹出瞬间的闪帧也就能忍
    private void ApplyWindowAlpha()
    {
        double alpha = PopupOpacity.Value;
        if (OverlayWindowService.TrySetNativeWindowAlpha(this, alpha)) return;
        if (Content is Control content) content.Opacity = alpha;
    }

    private void OnMainButtonClick(object? sender, RoutedEventArgs e)
    {
        AssistantExplainPromptAction skill = new AssistantExplainPromptAction();
        QuickChatResultWindow.Show(Loc.Text(LangKey.Explain), _answerString, skill);
        PlayAnimation(false, SafeClose);
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        //ignore
    }

    // 固定宽度窗口里进出由 CardHoverMask 管：窗口忽略鼠标事件后收不到 Exited，这里的也可能是过时的
    protected override void OnPointerExited(PointerEventArgs e)
    {
        if (UsesFixedWindowWidth) return;
        base.OnPointerExited(e);
    }

    protected override void OnMouseClicked(MouseEventData obj)
    {
        // 点击是否落在窗口上，以卡片为准：窗口本身比卡片宽，IsPointerOver 不可靠
        if (_hoverMask == null || !UsesFixedWindowWidth)
        {
            base.OnMouseClicked(obj);
            return;
        }

        if (!_hoverMask.IsInside) SafeClose();
    }

    private void OnMainButtonPointerEntered(object? sender, PointerEventArgs e)
    {
        if (MainMenu.IsVisible && MainMenu.Opacity >= 0.99) return;
        PlayAnimation(true);
    }

    protected override void PlayAnimation(bool isShowed, Action? onCompleted = null)
    {
        if (isShowed) ShowMenu(onCompleted);
        else HideMenu(onCompleted);
    }

    private bool _menuIsShown;

    // 菜单项图标固定浅色：背景是固定深色胶囊，跟随主题会在浅色主题下变黑（与文字 #E6FFFFFF 同色）
    private static readonly Color MenuIconColor = Color.Parse("#E6FFFFFF");

    // 窗口此刻应有的宽度，NaN 表示还没开始管理。Avalonia 在平台每次上报与当前不同的客户区尺寸时，
    // 会把 Window.Width 回写成上报值（Window.HandleResized），macOS 上 resize 期间上报的常是过时或过渡的尺寸，
    // 快速展开/收起时它会把我们刚设好的宽度悄悄改掉——改掉的宽度必须立刻恢复，不然窗口就是一根细条
    private double _windowWidth = double.NaN;

    // 卡片此刻的逻辑宽度，动画的起点。刻意不读 Bounds：窗口在 macOS 上 resize 期间 Bounds 可能是过渡值，
    // 鼠标来回晃动会不停打断动画，任何一次读到过渡值都会被当成起点/收起宽度存下来，卡片就被压成一根细条
    private double _cardWidth = double.NaN;

    /// <summary>
    /// 展开菜单：先恢复布局（窗口向右扩、主按钮原地不动），再播滑入动画。
    /// 已完全展开时直接跳过，不空转动画。
    /// </summary>
    private void ShowMenu(Action? onCompleted)
    {
        if (_menuIsShown)
        {
            onCompleted?.Invoke();
            return;
        }

        _menuIsShown = true;
        PlayMenuSlide(true, onCompleted);
        ClampWindowToScreenSoon();
    }

    /// <summary>
    /// 收起菜单：动画播完才把菜单从布局中移除（窗口缩回只含主按钮）。
    /// 已收起时直接跳过。收起播到一半又要展开时，前一个动画被取消、折叠不执行。
    /// </summary>
    private void HideMenu(Action? onCompleted)
    {
        if (!_menuIsShown)
        {
            onCompleted?.Invoke();
            return;
        }

        _menuIsShown = false;
        PlayMenuSlide(false, () =>
        {
            MainMenu.IsVisible = false;
            onCompleted?.Invoke();
        });
    }

    private const int MenuSlideMilliseconds = 150;
    private const double MenuSlideHiddenOffset = -14;

    private CancellationTokenSource? _menuSlideCts;

    /// <summary>
    /// 菜单显隐只做位移、不做淡出：Svg 图标的绘制绕过 Avalonia 合成层，父级 Opacity 只生效
    /// 0/1，淡出会让「文字在淡、图标不变」分裂，索性整个菜单一起滑走（方案 Y）。
    /// </summary>
    private void PlayMenuSlide(bool isShowed, Action? onCompleted = null)
    {
        _menuSlideCts?.Cancel();
        _menuSlideCts = new CancellationTokenSource();
        _ = PlayMenuSlideCoreAsync(isShowed, onCompleted, _menuSlideCts.Token);
    }

    private async Task PlayMenuSlideCoreAsync(bool isShowed, Action? onCompleted,
        CancellationToken ct)
    {
        try
        {
            if (MainMenu.RenderTransform is not TranslateTransform transform)
            {
                transform = new TranslateTransform(isShowed ? MenuSlideHiddenOffset : 0, 0);
                MainMenu.RenderTransform = transform;
            }

            MainMenu.IsHitTestVisible = isShowed;
            if (isShowed) MainMenu.IsVisible = true;

            // 逐帧动的是卡片自己的宽度（纯布局，窗口是透明的，多出来的那截看不见）。
            // 固定宽度窗口(macOS)全程不改原生尺寸；否则窗口宽度只在动画两端各改一次：
            // 展开先一步放到位，收起等动画播完再缩回。
            // 两端的宽度都由布局量出来，不读 Bounds（见 _cardWidth）
            double collapsedWidth = MeasureCardWidth(withMenu: false);
            double expandedWidth = MeasureCardWidth(withMenu: true);
            double startCardWidth = double.IsNaN(_cardWidth)
                ? collapsedWidth
                : Math.Clamp(_cardWidth, collapsedWidth, expandedWidth);
            double targetCardWidth = isShowed ? expandedWidth : collapsedWidth;
            Card.Width = startCardWidth;
            SizeToContent = SizeToContent.Height;
            // 动画期间窗口一直是展开宽度（收起也是播完才缩回）
            if (UsesFixedWindowWidth) GrowFixedWindowWidth(expandedWidth);
            else SetManagedWindowWidth(expandedWidth);

            double startX = transform.X;
            double targetX = isShowed ? 0 : MenuSlideHiddenOffset;

            // 跟显示帧走而不是 Task.Delay 循环：模型流式输出、转圈图标都在占 UI 线程，Delay 循环会被拖成隔几帧才动一下
            await UiAnimationUtils.RunFrameAnimationAsync(this, MenuSlideMilliseconds, progress =>
            {
                double eased = 1 - Math.Pow(1 - progress, 3);
                transform.X = Lerp(startX, targetX, eased);
                _cardWidth = Lerp(startCardWidth, targetCardWidth, eased);
                Card.Width = _cardWidth;
            }, ct);

            // 播完这一刻可能已被新动画取代：不检查的话旧动画的收尾会把新动画正在用的宽度和窗口尺寸改回去
            ct.ThrowIfCancellationRequested();
            transform.X = targetX;
            _cardWidth = targetCardWidth;
            if (!isShowed)
            {
                MainMenu.IsVisible = false;
                if (!UsesFixedWindowWidth) SetManagedWindowWidth(collapsedWidth);
            }

            Card.ClearValue(WidthProperty);
            onCompleted?.Invoke();
        }
        catch (OperationCanceledException)
        {
        }
    }

    protected override void OnPreClose()
    {
        base.OnPreClose();
        // 动画是一个不依赖窗口生命周期的异步循环：不取消的话，窗口关了它还会继续去改已关闭窗口的宽度
        _menuSlideCts?.Cancel();
        _hoverMask?.Dispose();
        if (!UsesFixedWindowWidth) _windowWidth = double.NaN;
    }

    // 只加宽不收窄：宽度一变就是一次原生尺寸变化，正是要避开的
    private void GrowFixedWindowWidth(double neededWidth)
    {
        if (neededWidth > _windowWidth) SetManagedWindowWidth(neededWidth);
    }

    private void SetManagedWindowWidth(double width)
    {
        _windowWidth = width;
        RestoreWindowWidth();
    }

    private void RestoreWindowWidth()
    {
        if (double.IsNaN(_windowWidth)) return;
        // Width 初始是 NaN（靠 SizeToContent 自适应），NaN 参与比较恒为 false，必须单独判
        if (double.IsNaN(Width) || Math.Abs(Width - _windowWidth) > 0.5) Width = _windowWidth;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // 不在属性变更回调里直接改：回写发生在平台 resize 通知的处理途中，排到随后再恢复
        if (change.Property == WidthProperty && !double.IsNaN(_windowWidth))
            Dispatcher.UIThread.Post(RestoreWindowWidth, DispatcherPriority.Send);
    }

    /// <summary>量卡片在菜单展开/折叠两种状态下的期望宽度（同时也是窗口对应的宽度）</summary>
    private double MeasureCardWidth(bool withMenu)
    {
        // 卡片可能正被动画固定着宽度、菜单也可能正显示着：先放开量，量完原样还回去
        double fixedWidth = Card.Width;
        bool menuWasVisible = MainMenu.IsVisible;
        Card.ClearValue(WidthProperty);
        MainMenu.IsVisible = withMenu;
        // 必须显式失效：同样的约束(Infinity)连量两次会命中「测量仍有效」而直接返回旧结果。
        // 菜单原本隐藏、没被测量过，切它的 IsVisible 也不会把失效传到卡片上
        Card.InvalidateMeasure();
        Card.Measure(Size.Infinity);
        double width = Card.DesiredSize.Width;
        MainMenu.IsVisible = menuWasVisible;
        if (!double.IsNaN(fixedWidth)) Card.Width = fixedWidth;
        return width;
    }

    private static double Lerp(double from, double to, double t)
    {
        return from + (to - from) * t;
    }

    // 展开让窗口变宽后，右缘可能超出屏幕：布局更新完把窗口钳回屏幕内（不动弹出位置，只做兜底）
    private void ClampWindowToScreenSoon()
    {
        Dispatcher.UIThread.Post(ClampWindowToScreen, DispatcherPriority.Loaded);
    }

    private void ClampWindowToScreen()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        // 无头/未初始化环境（App.ScreensService 未建）时跳过：钳制只是展开后的兜底，缺了不致命
        var screen = App.ScreensService?.MouseScreen;
        if (screen == null) return;
        // 固定宽度窗口比展开后的卡片宽，钳制要按卡片展开后的宽度，不然靠近右缘时被无谓地左推
        Size size = UsesFixedWindowWidth ? new Size(MeasureCardWidth(withMenu: true), Bounds.Height) : Bounds.Size;
        Position = UiUtils.EnsurePositionWithinScreen(screen, Position, size);
    }

    private void InitFunctionMenu()
    {
        FunctionMenu.Children.Clear();
        AddFunctionMenu(nameof(LangKey.Translation), "text-wrap",
            () =>
            {
                TranslationPromptAction skill = new TranslationPromptAction();
                QuickChatResultWindow.Show(Loc.Text(LangKey.Translation), _answerString, skill);
            });
        AddFunctionMenu(nameof(LangKey.SyntacticAnalysis), "scan-text",
            () => QuickChatResultWindow.Show(Loc.Text(LangKey.SyntacticAnalysis), _answerString, new AssistantSyntacticAnalysisPromptAction()));
        AddFunctionMenu(nameof(LangKey.Think), "brain",
            () => QuickChatResultWindow.Show(Loc.Text(LangKey.Think), _answerString, new ChainOfThoughtPromptAction()));
        AddFunctionMenu(nameof(LangKey.Ask), "message-circle-more",
            () => QuickStartChatWindow.Show(_answerString));
    }

    private void AddFunctionMenu(string textKey, string iconName, Action action, int xMargin = 4)
    {
        var btn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Children =
                {
                    // 图标固定浅色：背景是固定深色胶囊，图标跟随主题会在浅色主题下变黑、看不见
                    new ThemedSvgIcon { IconName = iconName, Width = 15, Height = 15, CurrentColor = MenuIconColor },
                    new TextBlock
                    {
                        Text = LocalizationManager.Instance.GetString(textKey),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Command = new RelayCommand(() =>
            {
                action();
                SafeClose();
            }),
            Margin = new Thickness(xMargin, 0, 0, 0),
            MinHeight = 25,
        };
        FunctionMenu.Children.Add(btn);
    }
}
