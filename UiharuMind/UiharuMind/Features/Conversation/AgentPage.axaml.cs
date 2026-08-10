/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using Avalonia.Controls;
using Avalonia.Input;

namespace UiharuMind.Features.Conversation;

public partial class AgentPage : UserControl
{
    public AgentPage()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is AgentPageData data) data.UpdateResponsiveState(e.NewSize.Width);
    }

    /// <summary>
    /// 展开前重建最近工作区列表：这份列表也会被设置页(默认工作目录)写入，
    /// 而那条路径不经过会话，光靠 WorkspacePath 变化刷新会漏掉。
    /// </summary>
    private void OnRecentWorkspacesOpening(object? sender, EventArgs e)
    {
        if (DataContext is AgentPageData data) data.Conversation.RefreshRecentWorkspaces();
    }

    private void OnLeftThumbDragDelta(object? sender, VectorEventArgs e)
    {
        var data = (AgentPageData)DataContext!;
        data.LeftPaneWidth = Math.Clamp(data.LeftPaneWidth + (float)e.Vector.X, 120, 400);
    }

    private void OnRightThumbDragDelta(object? sender, VectorEventArgs e)
    {
        var data = (AgentPageData)DataContext!;
        data.RightPaneWidth = Math.Clamp(data.RightPaneWidth - (float)e.Vector.X, 120, 400);
    }
}