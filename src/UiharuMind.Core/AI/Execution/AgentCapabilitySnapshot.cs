/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Mcp;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 本会话此刻<b>实际挂上</b>的能力。
///
/// 取的是装配产物本身（<c>AgentHandle</c> 上那份 <c>ChatOptions.Tools</c>），不是从能力开关
/// 二次推导——推导出来的东西迟早会与真相分叉，而这个面板存在的意义恰恰是回答
/// "模型现在到底看得见什么"。
/// </summary>
public sealed class AgentCapabilitySnapshot
{
    /// <summary>空快照（未挂接会话，或纯提示词档）</summary>
    public static readonly AgentCapabilitySnapshot Empty = new();

    /// <summary>自建工具（MCP 之外的那些）</summary>
    public IReadOnlyList<AgentToolInfo> BuiltInTools { get; init; } = [];

    /// <summary>MCP 侧的工具与自述，按 server 分组</summary>
    public McpToolSet Mcp { get; init; } = McpToolSet.Empty;

    /// <summary>
    /// 系统提示的分段清单（角色段、工具纪律、MCP 自述、工作区规矩），已带估算占用。
    /// 提示词与工具定义一样每轮完整重发，而它通常比工具还大——角色卡加一份 AGENTS.md
    /// 轻松几千 token，这笔账不摆出来，用户只会以为是工具太多
    /// </summary>
    public IReadOnlyList<AgentPromptSegment> PromptSegments { get; init; } = [];

    /// <summary>
    /// 每轮固定开销的估算合计：系统提示 + 工具定义（含 MCP 自述）。
    /// MCP 自述那一段不从 <see cref="PromptSegments"/> 计入——它已在 <see cref="Mcp"/> 里算过
    /// （见 <see cref="AgentPromptSegment.CountsTowardTotal"/>）
    /// </summary>
    public int EstimatedTokens =>
        BuiltInTools.Sum(x => x.EstimatedTokens) + Mcp.EstimatedTokens
        + Mcp.Groups.Sum(x => x.InstructionsEstimatedTokens)
        + PromptSegments.Where(x => x.CountsTowardTotal).Sum(x => x.EstimatedTokens);

    /// <summary>
    /// 取某一段系统提示的估算占用
    /// </summary>
    /// <param name="section">段别</param>
    /// <returns>估算 token；该段未出现在本次装配里则为 0</returns>
    public int PromptTokensOf(EPromptSection section)
    {
        int total = 0;
        foreach (AgentPromptSegment segment in PromptSegments)
        {
            if (segment.Section == section) total += segment.EstimatedTokens;
        }

        return total;
    }

    /// <summary>按能力档汇总的估算占用（角色编辑页据此显示「关掉这一档能省多少」）</summary>
    public IReadOnlyDictionary<EAgentCapability, int> TokensByCapability { get; init; } =
        new Dictionary<EAgentCapability, int>();

    /// <summary>
    /// 从一份已装配的工具集切出快照
    /// </summary>
    /// <param name="entries">装配好的工具，每项带能力归属</param>
    /// <param name="mcp">同一次装配的 MCP 产物</param>
    /// <param name="promptSegments">同一次装配登记的系统提示分段；在此统一分词</param>
    /// <returns>快照</returns>
    public static AgentCapabilitySnapshot Capture(IReadOnlyList<AgentToolEntry>? entries, McpToolSet mcp,
        IReadOnlyList<AgentPromptSegment>? promptSegments = null)
    {
        // 一个工具都没挂也要往下走:能力全关的 agent 仍有角色提示与工作区规矩要报,
        // 早退等于把提示词那半边账在「没工具」这种最该看清账的场合里藏掉
        List<AgentToolInfo> builtIn = new();
        Dictionary<EAgentCapability, int> byCapability = new();
        foreach (AgentToolEntry entry in entries ?? [])
        {
            int tokens = ToolTokenEstimator.Estimate(entry.Tool);
            byCapability[entry.Capability] = byCapability.GetValueOrDefault(entry.Capability) + tokens;

            // MCP 那批已经在 mcp 里按 server 分好组,不再重复列进自建工具
            if (entry.Capability == EAgentCapability.Mcp) continue;
            builtIn.Add(new AgentToolInfo
            {
                Name = entry.Tool.Name,
                Description = entry.Tool.Description,
                Capability = entry.Capability,
                EstimatedTokens = tokens,
            });
        }

        return new AgentCapabilitySnapshot
        {
            BuiltInTools = builtIn,
            Mcp = mcp,
            TokensByCapability = byCapability,
            // 分词在这里做,不在装配里:本方法跑在能力面板的后台刷新上,而装配在发消息路径上
            PromptSegments = promptSegments?
                .Select(x => x with { EstimatedTokens = ToolTokenEstimator.EstimateText(x.Text) })
                .ToList() ?? [],
        };
    }
}

/// <summary>
/// 一个自建工具的展示信息
/// </summary>
public sealed class AgentToolInfo
{
    /// <summary>工具名</summary>
    public required string Name { get; init; }

    /// <summary>工具描述</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>所属能力档（装配现场登记，不靠名字反推）</summary>
    public EAgentCapability Capability { get; init; }

    /// <summary>估算 token 数</summary>
    public int EstimatedTokens { get; init; }
}
