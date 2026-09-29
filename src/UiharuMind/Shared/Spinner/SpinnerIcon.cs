using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Shared.Spinner;

/// <summary>
/// 「忙」的转圈图标：一朵花，与菜单栏托盘图标是同一张素图，并且共用 <see cref="SpinClock"/>，
/// 所以两处转速、相位一致。应用内按显示帧连续旋转，不像托盘那样步进。
///
/// 素图是纯黑剪影，这里只取它的 alpha 当蒙版、再用 <see cref="Foreground"/> 着色。
/// 默认色是固定的 <see cref="RunningColor"/>，不跟主题走：它在明暗背景上都够清楚，
/// 「在跑」的标识也不需要随主题变化。使用处不需要任何样式或资源就是同一个转圈。
/// 只在挂在界面上且自身及所有祖先都可见时才逐帧刷新，隐藏的实例不产生任何开销。
/// 不用 <c>IsEffectivelyVisible</c>：它的变更事件在 Avalonia 12 里不公开，只能自己盯着祖先链的 <c>IsVisible</c>。
/// </summary>
public sealed class SpinnerIcon : Control
{
    private const double DefaultSize = 16;

    /// <summary>「在跑」的颜色。托盘的 Windows 角标共用它，两边才是同一个蓝</summary>
    public static readonly Color RunningColor = Color.Parse("#4C8DF6");

    // 静态字段按声明顺序初始化：DefaultBrush 依赖 RunningColor，必须排在它后面
    private static readonly IBrush DefaultBrush = new ImmutableSolidColorBrush(RunningColor);
    private static readonly Lazy<ImageBrush?> Mask = new(LoadMask);

    /// <summary>着色。不继承自父级文本前景色，默认 <see cref="RunningColor"/></summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<SpinnerIcon, IBrush?>(nameof(Foreground), DefaultBrush);

    private readonly RotateTransform _rotation = new();
    private readonly List<Visual> _watched = []; //自身与祖先链，挂上界面期间监听它们的 IsVisible
    private bool _attached;
    private bool _animating;
    private bool _frameRequested;

    /// <summary>着色</summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>是否正在逐帧刷新（挂在界面上且自身与祖先都可见）</summary>
    internal bool IsSpinning => _animating;

    static SpinnerIcon()
    {
        AffectsRender<SpinnerIcon>(ForegroundProperty);
        WidthProperty.OverrideDefaultValue<SpinnerIcon>(DefaultSize);
        HeightProperty.OverrideDefaultValue<SpinnerIcon>(DefaultSize);
        IsHitTestVisibleProperty.OverrideDefaultValue<SpinnerIcon>(false);
    }

    public SpinnerIcon()
    {
        RenderTransform = _rotation;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }

    public override void Render(DrawingContext context)
    {
        if (Foreground is not { } brush || Mask.Value is not { } mask) return;

        Rect bounds = new(Bounds.Size);
        using (context.PushOpacityMask(mask, bounds))
        {
            context.DrawRectangle(brush, null, bounds);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        for (Visual? visual = this; visual != null; visual = visual.GetVisualParent())
        {
            visual.PropertyChanged += OnWatchedPropertyChanged;
            _watched.Add(visual);
        }

        UpdateAnimating();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        foreach (Visual visual in _watched) visual.PropertyChanged -= OnWatchedPropertyChanged;
        _watched.Clear();
        UpdateAnimating();
    }

    /// <summary>按时间轴此刻的角度落一次姿态</summary>
    internal void Sync() => _rotation.Angle = SpinClock.Angle;

    private void OnWatchedPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty) UpdateAnimating();
    }

    private void UpdateAnimating()
    {
        bool shouldAnimate = _attached && _watched.TrueForAll(visual => visual.IsVisible);
        if (shouldAnimate == _animating) return;

        _animating = shouldAnimate;
        if (shouldAnimate)
        {
            Sync();
            RequestFrame();
        }
    }

    // 每个显示帧请求下一帧;_frameRequested 防止「停了又起」时叠出两条循环
    private void RequestFrame()
    {
        if (_frameRequested || TopLevel.GetTopLevel(this) is not { } topLevel) return;

        _frameRequested = true;
        topLevel.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan _)
    {
        _frameRequested = false;
        if (!_animating) return;

        Sync();
        RequestFrame();
    }

    private static ImageBrush? LoadMask() =>
        IconUtils.LoadDefaultBitmap("TrayFlowerIdle.png") is { } bitmap
            ? new ImageBrush(bitmap) { Stretch = Stretch.Uniform }
            : null;
}
