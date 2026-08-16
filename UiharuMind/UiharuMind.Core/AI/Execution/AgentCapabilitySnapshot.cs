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

    /// <summary>全部工具定义的估算 token 合计（含 MCP 自述）</summary>
    public int EstimatedTokens =>
        BuiltInTools.Sum(x => x.EstimatedTokens) + Mcp.EstimatedTokens
        + Mcp.Groups.Sum(x => x.InstructionsEstimatedTokens);

    /// <summary>按能力档汇总的估算占用（角色编辑页据此显示「关掉这一档能省多少」）</summary>
    public IReadOnlyDictionary<EAgentCapability, int> TokensByCapability { get; init; } =
        new Dictionary<EAgentCapability, int>();

    /// <summary>
    /// 从一份已装配的工具集切出快照
    /// </summary>
    /// <param name="entries">装配好的工具，每项带能力归属</param>
    /// <param name="mcp">同一次装配的 MCP 产物</param>
    /// <returns>快照</returns>
    public static AgentCapabilitySnapshot Capture(IReadOnlyList<AgentToolEntry>? entries, McpToolSet mcp)
    {
        if (entries == null || entries.Count == 0) return Empty;

        List<AgentToolInfo> builtIn = new();
        Dictionary<EAgentCapability, int> byCapability = new();
        foreach (AgentToolEntry entry in entries)
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
