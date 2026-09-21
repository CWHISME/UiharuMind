/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 这一轮卡在什么具名的事情上。要说的只有一件事：<b>没卡死，在忙别的</b>。
///
/// 刻意是一个枚举而不是几个并列的 bool：这些状态天然互斥、显示在同一处、共用一套形状，
/// 摊成 bool 的结果是每加一种就往 axaml 里复制一块 StackPanel，而且没有任何机制
/// 阻止两个 bool 同时为真。
///
/// ⚠️ <b>不带文案</b>。本地化属界面层，Core 只说发生了什么——与 <c>TurnNotice</c> 同一条规矩。
///
/// ⚠️ 作用域是<b>会话级</b>，不是进程级。「MCP server 正在连」确实是全进程共享的事实
/// （连接与工具缓存都在 <c>McpManager</c> 那个单例上），但要显示在输入框旁边的是
/// 「<b>我这一轮</b>正卡在等它」。曾经这里读的是单例上的一个全局 bool，于是后台定时任务
/// 触发的预连会点亮一个跟 MCP 毫无关系的扮演会话的提示，两条链路并发时还会互相把提示掐灭。
/// </summary>
public enum ETurnBusy
{
    /// <summary>不忙，或忙在不必单独说明的事情上（正常流式输出走 <c>IsRunning</c>）</summary>
    None,

    /// <summary>装配前等 MCP server 连上（<b>预连</b>，见 <c>McpManager.WarmupAsync</c>）</summary>
    ConnectingMcp,

    /// <summary>正在整理交接文档（多一次模型请求，见 <c>HistoryHandoff</c>）</summary>
    Compacting,
}
