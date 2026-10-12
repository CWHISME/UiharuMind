/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace UiharuMind.Features.Conversation.Pages;

/// <summary>
/// 对话类页面的五列骨架：会话列表 pane、拖条、顶栏 + 通用对话组件、拖条、侧栏。
/// 绑定契约是 <see cref="ConversationPageDataBase"/>，两页只经三个内容槽与一个开关定制。
///
/// ⚠️ 侧栏<b>只共享框</b>（宽度、开合、拖条、样式），<b>内容不共享</b>——
/// 聊天页是插件面板、智能体页是工作区与任务，见术语表「页面壳的组成」。
/// </summary>
public partial class ConversationPageShell : UserControl
{
    public static readonly StyledProperty<object?> LeftPaneHeaderProperty =
        AvaloniaProperty.Register<ConversationPageShell, object?>(nameof(LeftPaneHeader));

    public static readonly StyledProperty<object?> EmptyContentProperty =
        AvaloniaProperty.Register<ConversationPageShell, object?>(nameof(EmptyContent));

    public static readonly StyledProperty<object?> RightPaneContentProperty =
        AvaloniaProperty.Register<ConversationPageShell, object?>(nameof(RightPaneContent));

    public static readonly StyledProperty<bool> ShowSessionAvatarProperty =
        AvaloniaProperty.Register<ConversationPageShell, bool>(nameof(ShowSessionAvatar), true);

    /// <summary>会话列表上方的标题区(标题、副标题与新建按钮)。两页的新建按钮是两种控件形态，故整块交给宿主页</summary>
    public object? LeftPaneHeader
    {
        get => GetValue(LeftPaneHeaderProperty);
        set => SetValue(LeftPaneHeaderProperty, value);
    }

    /// <summary>会话为空时的引导内容，透传给 ConversationView</summary>
    public object? EmptyContent
    {
        get => GetValue(EmptyContentProperty);
        set => SetValue(EmptyContentProperty, value);
    }

    /// <summary>右侧栏内容</summary>
    public object? RightPaneContent
    {
        get => GetValue(RightPaneContentProperty);
        set => SetValue(RightPaneContentProperty, value);
    }

    /// <summary>会话列表条目是否显示头像</summary>
    public bool ShowSessionAvatar
    {
        get => GetValue(ShowSessionAvatarProperty);
        set => SetValue(ShowSessionAvatarProperty, value);
    }

    // 探针：右栏首次物化（主题六 Stage 1）——右栏面板树是 layout 245ms 的候选块，只记一次
    private bool _rightPaneProbed;
    private long _rightPaneBegin;

    // 主题六 B：右栏物化后置。Content 不绑定，属性变化排 Background 物化，首帧只出壳（空
    // ContentPresenter 占位，宽度固定无位移）；面板树在空闲时物化，用户命中右栏则同步物化。
    // 版本号作废过期排队（阿洛娜：切会话/页签时旧物化不能串到新会话）
    private int _materializeVersion;
    private bool _rightPaneMaterialized;

    public ConversationPageShell()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        _rightPaneBegin = global::UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Begin();
        RightPaneHost.Loaded += OnRightPaneFirstLoaded;
        RightPaneContentProperty.Changed.AddClassHandler<ConversationPageShell>((shell, e) => shell.OnRightPaneContentChanged(e));
        RightPaneHost.PointerPressed += (_, _) => SyncMaterializeNow();
    }

    /// <summary>RightPaneContent 变了：排一次 Background 物化（带版本号，过期作废）</summary>
    private void OnRightPaneContentChanged(AvaloniaPropertyChangedEventArgs e)
    {
        object? content = e.NewValue;
        int version = ++_materializeVersion;
        Dispatcher.UIThread.Post(() =>
        {
            if (version != _materializeVersion) return; //被更新的切换取代:旧物化作废
            ApplyRightPaneContent(content);
        }, DispatcherPriority.Background);
    }

    /// <summary>用户命中右栏但内容还没物化完：立即同步物化（等待策略，代价 200-400ms 实测量）</summary>
    internal void SyncMaterializeNow()
    {
        if (_rightPaneMaterialized) return;
        ++_materializeVersion; //作废还在排队的延迟任务
        ApplyRightPaneContent(RightPaneContent);
    }

    private void ApplyRightPaneContent(object? content)
    {
        RightPaneHost.Content = content;
        _rightPaneMaterialized = true;
        ProbeRightPaneTree();
    }

    // 面板树物化完成的探针：Content 落地后第一次布局（right-pane-materialize 量的是壳 Loaded，
    // B 后壳先出，面板树完成要单独量）。不退订：订阅挂在本控件上，生命周期一致；闭包标志保证只 End 一次
    private void ProbeRightPaneTree()
    {
        long treeBegin = global::UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Begin();
        bool done = false;
        RightPaneHost.LayoutUpdated += (_, _) =>
        {
            if (done) return;
            done = true;
            global::UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.End("page/right-pane-tree", treeBegin);
        };
    }

    private void OnRightPaneFirstLoaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_rightPaneProbed) return;
        _rightPaneProbed = true;
        global::UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.End("page/right-pane-materialize", _rightPaneBegin);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is ConversationPageDataBase data) data.UpdateResponsiveState(e.NewSize.Width);
    }

    private void OnLeftThumbDragDelta(object? sender, VectorEventArgs e)
    {
        if (DataContext is ConversationPageDataBase data) data.DragLeftPane(e.Vector.X);
    }

    private void OnRightThumbDragDelta(object? sender, VectorEventArgs e)
    {
        if (DataContext is ConversationPageDataBase data) data.DragRightPane(e.Vector.X);
    }
}
