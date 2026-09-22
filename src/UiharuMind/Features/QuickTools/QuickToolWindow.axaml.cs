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

    protected override void OnPostShow()
    {
        base.OnPostShow();
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

    // 收起时窗口要缩回的宽度（展开前记录，就是只有主按钮的宽度）
    private double _collapsedWindowWidth;

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

        _collapsedWindowWidth = Bounds.Width;
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

            // 窗口宽度全程手动 + 高度自适应：SizeToContent 来回切换会让 macOS 在 resize 时
            // 闪一帧拉伸/压扁（展开压扁、收起后抖动就是它）。开始先把当前宽度固化，之后只动画 Width。
            Width = Bounds.Width;
            SizeToContent = SizeToContent.Height;

            MainMenu.IsHitTestVisible = isShowed;
            if (isShowed) MainMenu.IsVisible = true;

            // 展开目标 = 菜单恢复布局后的内容期望宽度；收起目标 = 展开前宽度
            double startX = transform.X;
            double targetX = isShowed ? 0 : MenuSlideHiddenOffset;
            double startWidth = Bounds.Width;
            double targetWidth = isShowed ? MeasureExpandedWidth() : _collapsedWindowWidth;

            var startTime = DateTime.UtcNow;
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                double elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                double progress = Math.Clamp(elapsed / MenuSlideMilliseconds, 0, 1);
                double eased = 1 - Math.Pow(1 - progress, 3);

                transform.X = Lerp(startX, targetX, eased);
                Width = Lerp(startWidth, targetWidth, eased);

                if (progress >= 1) break;
                await Task.Delay(16, ct);
            }

            transform.X = targetX;
            Width = targetWidth;
            if (!isShowed) MainMenu.IsVisible = false;
            // 保持 SizeToContent=Height：收起后宽度固定，不再切回 WidthAndHeight 触发布局重算（抖动来源）
            onCompleted?.Invoke();
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>展开目标宽度：菜单恢复布局后，量卡片内容的期望宽度</summary>
    private double MeasureExpandedWidth()
    {
        Card.Measure(Size.Infinity);
        return Card.DesiredSize.Width;
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
        Position = UiUtils.EnsurePositionWithinScreen(screen, Position, Bounds.Size);
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
