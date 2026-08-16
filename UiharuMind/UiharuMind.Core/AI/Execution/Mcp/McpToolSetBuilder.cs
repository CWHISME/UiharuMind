/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.AI.Execution.Mcp;

/// <summary>
/// 已取到工具的一个 server，脱离 <see cref="McpManager"/> 的锁之后用于装配。
/// </summary>
/// <param name="Config">server 配置</param>
/// <param name="Tools">该 server 的工具</param>
/// <param name="Instructions">该 server 的自述</param>
internal readonly record struct ResolvedMcpServer(
    McpServerConfig Config,
    IReadOnlyList<AIFunction> Tools,
    string Instructions);

/// <summary>
/// 把若干 server 的工具并成一份可挂载的工具集。
///
/// <b>纯函数，不碰单例、不碰网络</b>——因此撞名改名这类容易写错又难在真机上复现的规则可以单测。
/// <see cref="McpManager"/> 只负责"取到哪些 server 的哪些工具"，怎么并是这里的事。
/// </summary>
internal static class McpToolSetBuilder
{
    /// <summary>
    /// 并成一份工具集。<b>撞名才改名</b>：默认保留 server 给的原名（短、模型好认），
    /// 只有同一个名字出现在两个 server 上时才给<b>双方</b>都加 server 前缀——
    /// 只改一边的话，"没被改的那个"是哪一个取决于遍历顺序，等于把不确定性藏得更深。
    /// 归属靠分组记账，不靠名字，所以不撞名时没有任何理由动它。
    /// </summary>
    /// <param name="resolved">已取到工具的 server</param>
    /// <returns>工具集、分组明细与拼好的自述</returns>
    public static McpToolSet Build(IReadOnlyList<ResolvedMcpServer> resolved)
    {
        if (resolved.Count == 0) return McpToolSet.Empty;

        // 先统计原名分布:只在跨 server 重复时才动名字
        Dictionary<string, int> nameOwners = new(StringComparer.Ordinal);
        foreach (ResolvedMcpServer server in resolved)
        {
            foreach (string name in server.Tools.Select(x => x.Name).Distinct(StringComparer.Ordinal))
            {
                nameOwners[name] = nameOwners.GetValueOrDefault(name) + 1;
            }
        }

        List<AITool> tools = new();
        List<McpServerToolGroup> groups = new(resolved.Count);
        StringBuilder instructions = new();
        int totalTokens = 0;

        foreach (ResolvedMcpServer server in resolved)
        {
            List<McpToolInfo> infos = new(server.Tools.Count);
            int groupTokens = 0;
            foreach (AIFunction tool in server.Tools)
            {
                bool collides = nameOwners.GetValueOrDefault(tool.Name) > 1;
                string finalName = collides ? $"{SanitizePrefix(server.Config.Name)}_{tool.Name}" : tool.Name;
                AIFunction mounted = collides ? new RenamedMcpFunction(tool, finalName) : tool;

                int tokens = ToolTokenEstimator.Estimate(mounted);
                groupTokens += tokens;
                tools.Add(mounted);
                infos.Add(new McpToolInfo
                {
                    Name = finalName,
                    OriginalName = tool.Name,
                    Description = tool.Description,
                    EstimatedTokens = tokens,
                });
            }

            bool injected = server.Config.InjectInstructions && server.Instructions.Length > 0;
            int instructionTokens = 0;
            if (injected)
            {
                instructionTokens = ToolTokenEstimator.EstimateText(server.Instructions);
                if (instructions.Length > 0) instructions.Append("\n\n");
                instructions.Append("## ").Append(server.Config.Name).Append('\n')
                    .Append(server.Instructions.TrimEnd());
            }

            totalTokens += groupTokens;
            groups.Add(new McpServerToolGroup
            {
                ServerName = server.Config.Name,
                Tools = infos,
                EstimatedTokens = groupTokens,
                InstructionsInjected = injected,
                InstructionsEstimatedTokens = instructionTokens,
            });
        }

        return new McpToolSet
        {
            Tools = tools,
            Groups = groups,
            Instructions = instructions.ToString(),
            EstimatedTokens = totalTokens,
        };
    }

    /// 工具名只允许字母数字下划线:前缀要拼进工具名,不能带 server 名里的空格与短横
    private static string SanitizePrefix(string serverName)
    {
        string cleaned = new(serverName.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        return string.IsNullOrEmpty(cleaned) ? "mcp" : cleaned;
    }
}

/// <summary>
/// 为消歧而改过名的 MCP 工具。除名字外一律透传给原工具。
/// </summary>
internal sealed class RenamedMcpFunction : DelegatingAIFunction
{
    private readonly string _name;

    public RenamedMcpFunction(AIFunction innerFunction, string name) : base(innerFunction)
    {
        _name = name;
    }

    public override string Name => _name;
}
