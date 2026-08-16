/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Avalonia.Controls;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 一次工具审批的卡片。DataContext 即 <see cref="ApprovalRequestItem"/>，由宿主的 DataTemplate 传入。
///
/// 与 <see cref="ToolCallCardView"/> 同一个理由抽成组件：模板与它依赖的样式原先只长在
/// AgentPage 内，别处（聊天页）要显示同一种条目就只能复制一份。
/// </summary>
public partial class ApprovalCardView : UserControl
{
    public ApprovalCardView()
    {
        InitializeComponent();
    }
}
