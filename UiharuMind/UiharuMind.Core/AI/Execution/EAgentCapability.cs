/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 一个工具属于哪一档能力开关。与 <c>AgentToolConfig</c> 的 <c>Enable*</c> 一一对应。
///
/// 存在的理由是<b>把占用算到开关头上</b>：角色编辑页要显示「关掉这一档能省多少 token」，
/// 而一档能力可能挂好几个工具（文件访问一次挂七个）。
/// </summary>
public enum EAgentCapability
{
    /// <summary>文件操作(Glob/Read/Write/Edit/Replace/Delete/Grep)</summary>
    FileAccess,

    /// <summary>Shell 执行</summary>
    Shell,

    /// <summary>网络搜索与抓取</summary>
    WebSearch,

    /// <summary>识图(委托视觉模型)</summary>
    VisionTool,

    /// <summary>知识库检索</summary>
    KnowledgeSearch,

    /// <summary>定时任务</summary>
    ScheduledTasks,

    /// <summary>子代理</summary>
    SubAgent,

    /// <summary>MCP server 提供的工具</summary>
    Mcp,
}

/// <summary>
/// 装配时记下的一个工具及其所属能力档。
///
/// <b>归属由装配现场记账，不靠事后按名字猜</b>——名字表会随工具改名腐烂，
/// 而这里是「谁把它加进来的，就登记在谁名下」，构造上不可能对不上。
/// </summary>
/// <param name="Capability">所属能力档</param>
/// <param name="Tool">工具</param>
public readonly record struct AgentToolEntry(EAgentCapability Capability, AITool Tool);
