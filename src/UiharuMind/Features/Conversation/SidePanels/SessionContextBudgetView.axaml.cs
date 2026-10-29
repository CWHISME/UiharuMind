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
/// 本会话上下文上限的编辑控件。DataContext 即 <see cref="SessionContextBudgetViewData"/>。
///
/// 只在输入框那个环的弹出面板里出现：右侧栏是只读的统计，
/// 混进一个能改上下文的控件会让「看一眼」变成「顺手改坏」。
/// </summary>
public partial class SessionContextBudgetView : UserControl
{
    public SessionContextBudgetView()
    {
        InitializeComponent();
    }
}
