using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// <see cref="ChunkedMarkdownViewer"/> 的一段。离视口远时段内容不参与布局，只按字数估一个高度占位：
/// 纯文本占位也要整段排字才量得出高度，200K 字全排一遍要一两秒，打开卡的就是这个。
/// 进到视口一屏以内才放出内容，此后不再收回；markdown 档直接显示排版结果，不经原文占位那一跳。
/// </summary>
internal sealed class MarkdownChunkSlot : Decorator
{
    private const double LineHeightFactor = 1.6;
    private const double NarrowCharFactor = 0.55;

    private readonly SimpleMarkdownViewer _viewer;
    private bool _isPlaintext;
    private bool _isActive; //已决定放出（看得见的立即显示，预取的排在后台）
    private bool _isRevealed; //段内容已显示、参与布局
    private bool _viewportSeen;
    private Rect _lastViewport; //最近一次视口（自身坐标系）；尺寸变了要拿它重判
    private double _anchorHeight = -1; //放出时顶端已滚出视口：记下估高，布局后按实际高度差补回滚动偏移

    public MarkdownChunkSlot(string text, bool isPlaintext, string? linkBaseDirectory)
    {
        _isPlaintext = isPlaintext;
        _viewer = new SimpleMarkdownViewer
        {
            MarkdownText = text,
            IsPlaintext = true, //未放出前渲染器不启动
            LinkBaseDirectory = linkBaseDirectory,
            IsVisible = false,
        };
        Child = _viewer;
        EffectiveViewportChanged += OnEffectiveViewportChanged;
    }

    public SimpleMarkdownViewer Viewer => _viewer;

    public bool IsPlaintext
    {
        get => _isPlaintext;
        set
        {
            _isPlaintext = value;
            if (!_isRevealed) return;
            _viewer.IsPlaintext = value;
            if (!value) _viewer.RealizeNow();
        }
    }

    /// <summary>立刻放出并显示（流式追加的尾段）</summary>
    public void Activate()
    {
        _isActive = true;
        Reveal();
    }

    // 看得见的段同步转 markdown 再显示（同聊天气泡 RealizeNow 的口径），第一帧就是排版后的样子；
    // 视口外预取的段保持估高、排到后台逐段转好再显示：一齐同步转的话代码块多的文档打开要多卡大半秒
    private void Release(bool isVisibleNow)
    {
        if (_isActive) return;
        _isActive = true;
        if (isVisibleNow || _isPlaintext)
        {
            Reveal();
            return;
        }

        Dispatcher.UIThread.Post(Reveal, DispatcherPriority.Background);
    }

    private void Reveal()
    {
        if (_isRevealed) return;
        _isRevealed = true;
        // 顶端已滚出视口：估高变实高会把可见内容顶走，记下估高，布局后补回滚动偏移
        if (_viewportSeen && _lastViewport.Y > 0)
        {
            _anchorHeight = Bounds.Height;
            LayoutUpdated += OnLayoutUpdatedAfterActivate;
        }

        _viewer.IsPlaintext = _isPlaintext;
        if (!_isPlaintext) _viewer.RealizeNow();
        _viewer.IsVisible = true;
        InvalidateMeasure();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        // 没有滚动容器的宿主收不到视口通知，布局跑过一轮还没动静就直接放出
        Dispatcher.UIThread.Post(() =>
        {
            if (!_viewportSeen) Release(isVisibleNow: true);
        }, DispatcherPriority.Background);
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        _viewportSeen = true;
        _lastViewport = e.EffectiveViewport;
        CheckNear();
    }

    // 视口通知可能先于自己量出高度（隐藏的复用窗里重建时就是这样），之后视口不变就不会再来，所以尺寸变了也要重判
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_viewportSeen) CheckNear();
    }

    private void CheckNear()
    {
        if (_isActive) return;
        // 只比竖直区间：居中排布时未放出的段可能零宽，矩形相交会恒为假
        Rect viewport = _lastViewport;
        double height = Bounds.Height;
        if (height <= 0) return;
        if (viewport.Y - viewport.Height < height && viewport.Bottom + viewport.Height > 0)
            Release(isVisibleNow: viewport.Y < height && viewport.Bottom > 0);
    }

    // 普通 StackPanel 不参与滚动锚定：视口上方的段由估高变实高，差多少可见内容就跳多少，这里手动补回
    private void OnLayoutUpdatedAfterActivate(object? sender, EventArgs e)
    {
        LayoutUpdated -= OnLayoutUpdatedAfterActivate;
        double delta = Bounds.Height - _anchorHeight;
        _anchorHeight = -1;
        if (Math.Abs(delta) < 0.5 || this.FindAncestorOfType<ScrollViewer>() is not { } scroll) return;
        scroll.Offset = scroll.Offset.WithY(scroll.Offset.Y + delta);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_isRevealed) return base.MeasureOverride(availableSize);
        bool isFinite = double.IsFinite(availableSize.Width);
        double width = isFinite ? availableSize.Width : 800;
        // 报满可用宽度：与放出后纯文本铺满的宽度一致，居中排布时也不会缩成零宽
        return new Size(isFinite ? width : 0, EstimateHeight(_viewer.MarkdownText ?? string.Empty, width, _viewer.FontSize));
    }

    private static double EstimateHeight(string text, double width, double fontSize)
    {
        if (fontSize <= 0) fontSize = 14;
        double maxLineWidth = Math.Max(width, fontSize * 4);
        int lines = 0;
        double lineWidth = 0;
        foreach (char c in text)
        {
            if (c == '\n')
            {
                lines += Math.Max(1, (int)Math.Ceiling(lineWidth / maxLineWidth));
                lineWidth = 0;
                continue;
            }

            lineWidth += c >= 0x2E80 ? fontSize : fontSize * NarrowCharFactor;
        }

        lines += Math.Max(1, (int)Math.Ceiling(lineWidth / maxLineWidth));
        return lines * fontSize * LineHeightFactor;
    }
}
