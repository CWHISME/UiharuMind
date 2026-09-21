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

    public ConversationPageShell()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
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
