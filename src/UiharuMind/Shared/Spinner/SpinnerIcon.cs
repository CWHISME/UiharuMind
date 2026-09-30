using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SkiaSharp;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Shared.Spinner;

/// <summary>
/// 「忙」的转圈图标：一朵花，与菜单栏托盘图标是同一张素图，并且共用 <see cref="SpinClock"/>，
/// 所以两处转速、相位一致。应用内按显示帧连续旋转，不像托盘那样步进。
///
/// 素图是纯黑剪影，启动时一次性染成固定的 <see cref="RunningColor"/>，不跟主题走：它在明暗背景上都够清楚，
/// 「在跑」的标识也不需要随主题变化。使用处不需要任何样式或资源就是同一个转圈。
///
/// 与托盘帧一样绕素图的 alpha 质心转：花的质心摆在控件中心，<c>RotateTransform</c> 绕控件中心转即绕质心转。
/// 每帧只改角度、不重绘，旋转交给合成器。不用不透明度蒙版着色：蒙版的位置会被取整到整像素，
/// 质心摆位的那零点几像素随之丢掉，转起来重心就在画小圈。
/// 只在挂在界面上且自身及所有祖先都可见时才逐帧刷新，隐藏的实例不产生任何开销。
/// 不用 <c>IsEffectivelyVisible</c>：它的变更事件在 Avalonia 12 里不公开，只能自己盯着祖先链的 <c>IsVisible</c>。
/// </summary>
public sealed class SpinnerIcon : Control
{
    private const double DefaultSize = 16;

    /// <summary>「在跑」的颜色。托盘的 Windows 角标共用它，两边才是同一个蓝</summary>
    public static readonly Color RunningColor = Color.Parse("#4C8DF6");

    private static readonly Lazy<SpinnerArt?> Art = new(LoadArt);

    private readonly RotateTransform _rotation = new();
    private readonly List<Visual> _watched = []; //自身与祖先链，挂上界面期间监听它们的 IsVisible
    private bool _attached;
    private bool _animating;
    private bool _frameRequested;

    /// <summary>是否正在逐帧刷新（挂在界面上且自身与祖先都可见）</summary>
    internal bool IsSpinning => _animating;

    static SpinnerIcon()
    {
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
        if (Art.Value is not { } art) return;

        PixelSize pixels = art.Bitmap.PixelSize;
        double scale = Math.Min(Bounds.Width / pixels.Width, Bounds.Height / pixels.Height);
        context.DrawImage(art.Bitmap, new Rect(
            Bounds.Width / 2 - art.Centroid.X * scale,
            Bounds.Height / 2 - art.Centroid.Y * scale,
            pixels.Width * scale,
            pixels.Height * scale));
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

    private static SpinnerArt? LoadArt()
    {
        try
        {
            using SKBitmap source = SkiaBitmapUtils.DecodeAsset("TrayFlowerIdle.png");
            (float x, float y) = SkiaBitmapUtils.AlphaCentroid(source);
            return new SpinnerArt(Tint(source, new SKColor(RunningColor.R, RunningColor.G, RunningColor.B)), new Point(x, y));
        }
        catch (Exception e)
        {
            Log.Error($"Load spinner art failed: {e.Message}");
            return null;
        }
    }

    // 只留 alpha、整张换成同一个颜色
    private static Bitmap Tint(SKBitmap source, SKColor color)
    {
        using SKBitmap tinted = new(source.Width, source.Height);
        using (SKCanvas canvas = new(tinted))
        using (SKPaint paint = new() { ColorFilter = SKColorFilter.CreateBlendMode(color, SKBlendMode.SrcIn) })
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(source, 0, 0, paint);
        }

        using SKData png = tinted.Encode(SKEncodedImageFormat.Png, 100);
        using MemoryStream stream = new(png.ToArray());
        return new Bitmap(stream);
    }

    /// <summary>染好色的素图与它的 alpha 质心（像素坐标）</summary>
    private sealed record SpinnerArt(Bitmap Bitmap, Point Centroid);
}
