/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Avalonia.Controls;
using UiharuMind.Features.Conversation.Pages;

namespace UiharuMind.Features.Conversation.Items;

/// <summary>
/// 一次工具调用的卡片。DataContext 即 <see cref="ToolCallItem"/>，由宿主的 DataTemplate 传入。
///
/// 会话流与子代理过程窗口共用这一份：两处不共享样式作用域
/// （<c>tool-card</c> 等原先只定义在 AgentPage 内），各写一份模板的结果就是两边观感不一致。
/// </summary>
public partial class ToolCallCardView : UserControl
{
    public ToolCallCardView()
    {
        InitializeComponent();
    }
}
