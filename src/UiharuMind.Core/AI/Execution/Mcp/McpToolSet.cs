/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.Mcp;

/// <summary>
/// 一次装配从 MCP 侧取到的全部东西：工具集、要拼进系统提示的 server 自述、
/// 以及按 server 分组的明细。
///
/// 三样合成一个返回值而不是三个方法，是因为它们必须<b>同出一刻</b>：
/// 工具是那一刻的缓存快照，自述属于同一批 server，分组明细则是这两者的归属账本。
/// 分开取会让右栏展示的与实际挂上的对不上号。
/// </summary>
public sealed class McpToolSet
{
    /// <summary>空集(未启用任何 server,或全被角色禁掉)</summary>
    public static readonly McpToolSet Empty = new();

    /// <summary>
    /// 拍平后的工具集,直接汇入 ChatOptions.Tools。
    /// 直挂 server 的工具，外加（存在按需 server 时）恒定的元工具 McpHelp / McpCall。
    /// </summary>
    public IReadOnlyList<AITool> Tools { get; init; } = [];

    /// <summary>
    /// 按需 server 名单（<b>只含配置里就有的信息</b>：名字与说明）。
    /// 非空即意味着 <see cref="Tools"/> 里带着两个元工具，且 <see cref="Instructions"/> 里有对应名单。
    /// </summary>
    public IReadOnlyList<McpOnDemandServer> OnDemandServers { get; init; } = [];

    /// <summary>按 server 分组的明细(右栏「能力」面板的数据源)</summary>
    public IReadOnlyList<McpServerToolGroup> Groups { get; init; } = [];

    /// <summary>已拼好的 server 自述段;无则空串</summary>
    public string Instructions { get; init; } = string.Empty;

    /// <summary>
    /// 估算 token 总数：直挂工具的定义，加上按需模式的固定开销
    /// （两个元工具的定义 + 系统提示里的按需名单）。
    /// </summary>
    public int EstimatedTokens { get; init; }
}

/// <summary>
/// 一个按需 server 在系统提示里的样子：名字与用户填的一句话说明。
/// 刻意<b>不带工具数与工具名</b>——那是运行时信息，会让系统提示随连接状态变化。
/// </summary>
public sealed class McpOnDemandServer
{
    /// <summary>server 名</summary>
    public required string Name { get; init; }

    /// <summary>项目级来源的工作区路径；全局 server 为 <c>null</c></summary>
    public string? WorkspacePath { get; init; }

    /// <summary>一句话说明；空即只写名字。项目级 server 恒为空</summary>
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// 一个 server 在本次装配里贡献了什么
/// </summary>
public sealed class McpServerToolGroup
{
    /// <summary>server 名</summary>
    public required string ServerName { get; init; }

    /// <summary>
    /// 项目级来源的工作区路径；全局 server 为 <c>null</c>。
    /// 面板据此问对那一条运行态——两个项目里各有一个同名 server 时，光靠名字会问到别人的状态。
    /// </summary>
    public string? WorkspacePath { get; init; }

    /// <summary>该 server 贡献的工具</summary>
    public required IReadOnlyList<McpToolInfo> Tools { get; init; }

    /// <summary>工具定义的估算 token 数</summary>
    public int EstimatedTokens { get; init; }

    /// <summary>该 server 的送达方式；按需的组没有工具明细（工具由 McpHelp 现查）</summary>
    public EMcpMountMode MountMode { get; init; } = EMcpMountMode.Direct;

    /// <summary>自述是否已注入系统提示</summary>
    public bool InstructionsInjected { get; init; }

    /// <summary>自述的估算 token 数(未注入时为 0)</summary>
    public int InstructionsEstimatedTokens { get; init; }

    /// <summary>本组的估算 token 合计</summary>
    public int TotalEstimatedTokens => EstimatedTokens + InstructionsEstimatedTokens;
}

/// <summary>
/// 一个 MCP 工具的展示信息
/// </summary>
public sealed class McpToolInfo
{
    /// <summary>模型看到的名字。与 <see cref="OriginalName"/> 不同即表示为消歧改过名</summary>
    public required string Name { get; init; }

    /// <summary>server 原本给的名字</summary>
    public required string OriginalName { get; init; }

    /// <summary>工具描述</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>估算 token 数</summary>
    public int EstimatedTokens { get; init; }

    /// <summary>是否因与别的 server 撞名而被加了前缀</summary>
    public bool IsRenamed => !string.Equals(Name, OriginalName, StringComparison.Ordinal);
}
