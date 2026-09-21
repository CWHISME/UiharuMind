/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Avalonia.Controls;

namespace UiharuMind.Features.Conversation.Items;

/// <summary>
/// 思考过程的折叠条。DataContext 即 <see cref="ThinkingItem"/>，由宿主的 DataTemplate 传入。
///
/// 抽成组件的理由与 <see cref="ToolCallCardView"/> 同：它同时长在会话流与子代理过程窗口里，
/// 而这两处原先各写一份 Expander 模板、Margin 还不一样——同一个东西两处观感，
/// 且都与相邻的工具卡片对不齐。
/// </summary>
public partial class ThinkingCardView : UserControl
{
    public ThinkingCardView()
    {
        InitializeComponent();
    }
}
