/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Avalonia.Controls;

namespace UiharuMind.Features.Conversation.SidePanels;

/// <summary>
/// 上下文占用的悬停面板。DataContext 即 <see cref="ContextUsageViewData"/>。
///
/// 数据那一半早就是独立类了，只有这块 axaml 一直内联在 ConversationView 的
/// <c>ToolTip.Tip</c> 里，连同六条只有它用的进度条样式一起，占了那个文件近百行。
/// </summary>
public partial class ContextUsagePanel : UserControl
{
    public ContextUsagePanel()
    {
        InitializeComponent();
    }
}
