/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 搜索跳到的那张卡亮一下：在卡片主体外描一圈强调色、晕开一点，随后淡掉（见 ADR 0056）。
///
/// 画在装饰层上的<b>一个</b>覆盖框，不改卡片本身：不占布局（卡片视口的等高占位要求容器高度只由卡片决定），
/// 也不必给几百张卡各挂一份常驻的阴影与过渡。卡片主体由各卡片视图用 <see cref="SurfaceClass"/> 标出——
/// 一张卡里可能有好几个气泡（图片、正文、技能全文），只亮正文那一块；没标的退回整个容器
/// </summary>
internal sealed class ConversationSearchHighlight
{
    /// <summary>卡片视图给自己主体那个 Border 挂的类</summary>
    public const string SurfaceClass = "search-surface";

    private const string AccentKey = "SemiBlue5";
    private const double RingGap = 3; //框在卡片外留的缝,圆角跟着放大同样多
    private static readonly TimeSpan HoldDuration = TimeSpan.FromSeconds(1.4);
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(400);

    private Visual? _target;
    private Border? _ring;

    /// <summary>亮一下这张卡（上一张还亮着就先撤掉）</summary>
    /// <param name="container">命中条目的容器</param>
    public void Show(Control container)
    {
        Hide();
        Control target = container.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(x => x.Classes.Contains(SurfaceClass) && x.IsEffectivelyVisible) ?? container;
        if (AdornerLayer.GetAdornerLayer(target) == null) return;

        Border ring = CreateRing(target);
        AdornerLayer.SetAdorner(target, ring);
        _target = target;
        _ring = ring;
        // 挂上之后的下一拍再亮:同一拍里改透明度不会走过渡
        Dispatcher.UIThread.Post(() => ring.Opacity = 1, DispatcherPriority.Render);
        DispatcherTimer.RunOnce(() => ring.Opacity = 0, HoldDuration);
        DispatcherTimer.RunOnce(() =>
        {
            if (ReferenceEquals(_ring, ring)) Hide();
        }, HoldDuration + FadeDuration);
    }

    private void Hide()
    {
        if (_target != null && ReferenceEquals(AdornerLayer.GetAdorner(_target), _ring)) AdornerLayer.SetAdorner(_target, null);
        _target = null;
        _ring = null;
    }

    private static Border CreateRing(Control target)
    {
        Color accent = target.TryFindResource(AccentKey, target.ActualThemeVariant, out object? resource) &&
                       resource is ISolidColorBrush brush
            ? brush.Color
            : Colors.CornflowerBlue;
        double radius = (target is Border border ? border.CornerRadius.TopLeft : 10) + RingGap;
        return new Border
        {
            IsHitTestVisible = false,
            Margin = new Thickness(-RingGap),
            CornerRadius = new CornerRadius(radius),
            BorderThickness = new Thickness(2),
            BorderBrush = new SolidColorBrush(accent),
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 16, Spread = 1, Color = Color.FromArgb(0x60, accent.R, accent.G, accent.B) }),
            Opacity = 0,
            Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = FadeDuration }],
        };
    }
}
