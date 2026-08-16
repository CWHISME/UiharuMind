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
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using LiveMarkdown.Avalonia;
using TextMateSharp.Grammars;
using UiharuMind.Shared.Services;

namespace UiharuMind.Shared.Controls;

public partial class SimpleMarkdownViewer : UserControl
{
    /// <summary>
    /// 纯文本档：用一块纯文本代替 markdown 渲染。它是<b>用户偏好</b>，不该按
    /// "是否生成完毕"来切——正在看的恰恰是正在生成的那一条，延后渲染最难受的就是它。
    /// 勾上时渲染器一次都不会启动（见 <see cref="Realize"/>）。
    /// </summary>
    public static readonly StyledProperty<bool> IsPlaintextProperty =
        AvaloniaProperty.Register<SimpleMarkdownViewer, bool>(nameof(IsPlaintext));

    public static readonly StyledProperty<string> MarkdownTextProperty =
        AvaloniaProperty.Register<SimpleMarkdownViewer, string>(nameof(MarkdownText));

    public bool? IsPlaintext
    {
        get => GetValue(IsPlaintextProperty);
        set => SetValue(IsPlaintextProperty, value);
    }

    public string MarkdownText
    {
        get => GetValue(MarkdownTextProperty);
        set => SetValue(MarkdownTextProperty, value);
    }

    /// <summary>
    /// 排队等待启用渲染器的控件。全场一个队列，每帧只放行一个——
    /// 切一次会话会同时冒出一窗气泡，一次性全建就是那一下明显的冻结
    /// </summary>
    private static readonly Queue<SimpleMarkdownViewer> PendingRealize = new();

    private static bool _pumpScheduled;

    private bool _isPlaintextCache = true;
    private bool _isRealized; //渲染器是否已接上内容
    private bool _isQueued; //已在 PendingRealize 里排队
    private bool _viewportSeen; //收到过视口通知(没有的话由兜底直接放行)
    private Rect _lastViewport; //最近一次视口矩形(自身坐标系);切档时据此判断"现在看得见吗"

    private ObservableStringBuilder _markdownBuilder = new ObservableStringBuilder();

    /// 纯文本块此刻是否顶在前面:纯文本档，或 markdown 档但渲染器还没接上
    private bool IsPlainTextShown => _isPlaintextCache || !_isRealized;

    /// 此刻是否落在视口里。收到过视口通知才作数——没通知时那份矩形是空的，
    /// 而"没通知"要走的是兜底那条路，不是判定成看不见
    private bool IsInViewport => _viewportSeen && _lastViewport.Intersects(new Rect(Bounds.Size));

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        UpdateCodeBlockTheme();
        EffectiveViewportChanged += OnEffectiveViewportChanged;

        // 兜底:没有滚动容器的宿主(帮助页之类)可能一次视口通知都收不到,
        // 那样 markdown 档会永远停在纯文本上。等布局跑过一轮还没动静就直接排队
        Dispatcher.UIThread.Post(() =>
        {
            if (!_viewportSeen) RequestRealize();
        }, DispatcherPriority.Background);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        EffectiveViewportChanged -= OnEffectiveViewportChanged;
    }

    /// <summary>
    /// 进入视口才启用 markdown 渲染。<b>这是切会话时那一下卡顿的正解</b>：
    /// 历史开窗有 20 条，屏幕上通常只看得见三五条，其余的视觉树建了也没人看。
    ///
    /// 判据是"看得见看不见"而不是"生成完没完"——用户盯着的那几条恰恰最先转。
    /// </summary>
    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        _viewportSeen = true;
        _lastViewport = e.EffectiveViewport;
        if (_isRealized || _isPlaintextCache) return;
        // 视口矩形与自身相交即视为看得见。纯文本块此刻已经把高度撑出来了,
        // 所以这个判断落在真实几何上,不是在一堆零高度的空壳上猜
        if (IsInViewport) RequestRealize();
    }

    private void RequestRealize()
    {
        if (_isRealized || _isQueued || _isPlaintextCache) return;
        _isQueued = true;
        PendingRealize.Enqueue(this);
        SchedulePump();
    }

    private static void SchedulePump()
    {
        if (_pumpScheduled) return;
        _pumpScheduled = true;
        Dispatcher.UIThread.Post(Pump, DispatcherPriority.Background);
    }

    /// 一帧只真正启用一个:剩下的排到下一帧,滚动与输入因此始终有机会插进来
    private static void Pump()
    {
        _pumpScheduled = false;
        while (PendingRealize.Count > 0)
        {
            SimpleMarkdownViewer viewer = PendingRealize.Dequeue();
            viewer._isQueued = false;
            if (viewer.Realize()) break; //真干了活就让出这一帧;跳过的不算数,继续找下一个
        }

        if (PendingRealize.Count > 0) SchedulePump();
    }

    /// <summary>
    /// 把累积的正文交给渲染器。<b>接上之前渲染器一个视觉对象都不会建</b>——
    /// 这也是纯文本档真正省下的东西
    /// </summary>
    /// <returns>真的启用了返回 true；已启用或已切回纯文本档则返回 false</returns>
    private bool Realize()
    {
        if (_isRealized || _isPlaintextCache) return false;
        _isRealized = true;
        MarkdownTextRender.MarkdownBuilder = _markdownBuilder;
        ApplyDisplayMode();
        return true;
    }

    private void ApplyDisplayMode()
    {
        bool plain = IsPlainTextShown;
        PlainTextBlock.IsVisible = plain;
        MarkdownTextRender.IsVisible = !plain;
        if (plain) PlainTextBlock.Text = _markdownBuilder.ToString();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownTextProperty)
        {
            ForceSetText(change.GetNewValue<string>());
        }
        else if (change.Property == IsPlaintextProperty)
        {
            _isPlaintextCache = change.GetNewValue<bool>();
            ApplyDisplayMode();
            // 用户当场取消纯文本档:看得见的那几条要立刻转,而切档本身不会再来一次视口通知。
            // 必须带上"看得见"这个条件——构造时绑定就会把本属性置为 false,
            // 不带条件的话一窗气泡会在那一刻全部排队,视口延迟就白做了
            if (!_isPlaintextCache && !_isRealized && IsInViewport) RequestRealize();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var maxWidth = Math.Clamp(availableSize.Width - 30, 0, int.MaxValue);
        MarkdownTextRender.MaxWidth = Math.Clamp(maxWidth - 1, 0, int.MaxValue);
        MainPanel.MaxWidth = maxWidth;
        return base.MeasureOverride(availableSize);
    }

    public SimpleMarkdownViewer()
    {
        InitializeComponent();

        IsPlaintext = true;
        // var currentTheme = Application.Current.ActualThemeVariant;
        // var fontFamily = currentTheme.<FontFamily>("FontFamily");
        // FontManager.Current.DefaultFontFamily
        // MarkdownTextBlock.Container.AddFontFamily(new FontFamily(
        //     new Uri("avares://UiharuMind/Assets/Fonts"),
        //     "#Dream Han Sans CN"));
        // MarkdownTextBlock.Container.AddFontFamily(new FontFamily(
        //     new Uri("avares://UiharuMind/Assets/Fonts"),
        //     "#JetBrains Mono"));
        // MarkdownTextBlock.StylesheetLoad += OnStylesheetLoad;
        // MarkdownTextBlock.Container.AddFontFamily(PlainTextBlock.FontFamily);
        // MarkdownTextBlock.AddHandler(
        //     MarkdownRenderer.LinkCommandProperty,
        //     new EventHandler<LinkClickedEventArgs>(OnLinkClick),
        //     handledEventsToo: true);

        // _stopwatch.Start();
        // _scrollViewerAutoScrollHolder =
        //     new ScrollViewerAutoScrollHolder((ScrollViewer)this.LogicalChildren[0].LogicalChildren[0]);

        if (Application.Current != null) Application.Current.ActualThemeVariantChanged += OnThemeChanged;
    }

    public void ForceSetText(string text)
    {
        text ??= "";

        // 以 _markdownBuilder 的实际内容为准,不另外维护一份镜像字符串——
        // 该控件有 ForceSetText 与 AppendText 两个写入口,任何镜像状态都要求两处同步维护,
        // 漏一处就会静默失效(曾表现为新会话清不掉上一次的内容)。
        string current = _markdownBuilder.ToString();
        if (text == current)
        {
            if (IsPlainTextShown) PlainTextBlock.Text = text;
            return;
        }

        // 传入累积全文时只追加增量:LiveMarkdown 的 ObservableStringBuilder 本就是为
        // 增量追加设计的,每次 Clear + Append 全文会让它在每个 token 上重解析整篇
        // 并重建视觉树,成本随长度二次增长。
        if (text.Length > current.Length && text.StartsWith(current, StringComparison.Ordinal))
        {
            _markdownBuilder.Append(text[current.Length..]);
        }
        else
        {
            _markdownBuilder.Clear();
            _markdownBuilder.Append(text);
        }

        if (IsPlainTextShown) PlainTextBlock.Text = text;
    }

    public void AppendText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        _markdownBuilder.Append(text);
        if (IsPlainTextShown) PlainTextBlock.Text = _markdownBuilder.ToString();
    }

    public void Clear()
    {
        ForceSetText("");
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        UpdateCodeBlockTheme();
    }

    private void UpdateCodeBlockTheme()
    {
        MarkdownTextRender.CodeBlockColorTheme = ApplicationThemeManager.IsDarkTheme()
            ? ThemeName.DarkPlus
            : ThemeName.LightPlus;
    }
}