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
using UiharuMind.Core.AI.Execution.Prompts;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.AI.Execution.Files;

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
    /// <param name="resolved">已取到工具的直挂 server</param>
    /// <param name="onDemand">按需 server（只含配置信息）；非空时挂上元工具并写入名单</param>
    /// <param name="metaTools">元工具（McpHelp / McpCall）；仅在 <paramref name="onDemand"/> 非空时使用</param>
    /// <param name="spillDirectory">
    /// 直挂工具结果的落盘目录；给了就给每个直挂工具套上结果整形（解包 CallToolResult、超限落盘），
    /// null 则原样挂（纯函数单测用）
    /// </param>
    /// <param name="paths">落盘目录是会话房间时的路径口径；null 写绝对路径</param>
    /// <returns>工具集、分组明细与拼好的自述</returns>
    public static McpToolSet Build(IReadOnlyList<ResolvedMcpServer> resolved,
        IReadOnlyList<McpOnDemandServer>? onDemand = null, IReadOnlyList<AIFunction>? metaTools = null,
        string? spillDirectory = null, AgentPathResolver? paths = null)
    {
        onDemand ??= [];
        if (resolved.Count == 0 && onDemand.Count == 0) return McpToolSet.Empty;

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
            ToolTokenBreakdown groupParts = default;
            foreach (AIFunction tool in server.Tools)
            {
                bool collides = nameOwners.GetValueOrDefault(tool.Name) > 1;
                string finalName = collides ? $"{SanitizePrefix(server.Config.Name)}_{tool.Name}" : tool.Name;
                AIFunction mounted = collides ? new RenamedMcpFunction(tool, finalName) : tool;
                if (spillDirectory != null) mounted = new McpResultFunction(mounted, spillDirectory, server.Config.Name, paths);

                ToolTokenBreakdown parts = ToolTokenEstimator.Breakdown(mounted);
                groupParts = new ToolTokenBreakdown(groupParts.Name + parts.Name,
                    groupParts.Description + parts.Description, groupParts.Schema + parts.Schema);
                int tokens = parts.Total;
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

            LogBreakdown(server, infos, groupTokens, groupParts);
            totalTokens += groupTokens;
            groups.Add(new McpServerToolGroup
            {
                ServerName = server.Config.Name,
                WorkspacePath = server.Config.WorkspacePath,
                Tools = infos,
                EstimatedTokens = groupTokens,
                InstructionsInjected = injected,
                InstructionsEstimatedTokens = instructionTokens,
            });
        }

        totalTokens += AppendOnDemand(onDemand, metaTools, tools, groups, instructions);

        return new McpToolSet
        {
            Tools = tools,
            OnDemandServers = onDemand,
            Groups = groups,
            Instructions = instructions.ToString(),
            EstimatedTokens = totalTokens,
        };
    }

    /// <summary>
    /// 并入按需部分：元工具、系统提示里的名单、每个 server 一条无工具明细的分组。
    /// 返回它带来的固定开销（元工具定义 + 名单文本），计入总占用——按需省的是工具定义，
    /// 但这笔恒定的开销也得让用户看见。
    /// </summary>
    private static int AppendOnDemand(IReadOnlyList<McpOnDemandServer> onDemand, IReadOnlyList<AIFunction>? metaTools,
        List<AITool> tools, List<McpServerToolGroup> groups, StringBuilder instructions)
    {
        if (onDemand.Count == 0) return 0;

        int overhead = 0;
        if (metaTools != null)
        {
            tools.AddRange(metaTools);
            overhead += ToolTokenEstimator.Estimate(metaTools);
        }

        string roster = AgentToolPrompts.BuildMcpOnDemand(
            onDemand.Select(x => (x.Name, x.Description)).ToList());
        if (instructions.Length > 0) instructions.Append("\n\n");
        instructions.Append(roster);
        overhead += ToolTokenEstimator.EstimateText(roster);

        foreach (McpOnDemandServer server in onDemand)
        {
            groups.Add(new McpServerToolGroup
            {
                ServerName = server.Name,
                WorkspacePath = server.WorkspacePath,
                MountMode = EMcpMountMode.OnDemand,
                Tools = [],
            });
        }

        return overhead;
    }

    /// <summary>
    /// 按构成打一条估算明细。
    ///
    /// 「贵在哪一段」是估算与服务端实报对不上时唯一能往下查的线索——描述特别长、schema 本身就大、
    /// 还是分词在 JSON 上吃亏，三者的处置完全不同。字符数与 token 数一并给正是为了分开后两者：
    /// 英文 JSON 通常 3~4 字符一个 token，明显低于这个比例就说明是分词的问题而非内容的问题。
    /// 定位 GLM4-Flash 少报工具定义（见 ADR 0009）靠的正是这两个数。
    ///
    /// ⚠️ <b>这是开发者诊断设施，日志就是它的终点，不上能力面板。</b>
    /// 检验的问题是可行动性：用户看到「schema 占 18080、desc 占 2733」，能做的还是「关掉这个 server」
    /// ——跟看到「这个 server 占 21232」是同一个决策，而 schema 是 server 作者写的，用户改不了。
    /// 真正可行动的粒度是 server 级（可禁用）与自述（<c>InjectInstructions</c> 可单独关），两级都已经有了。
    /// </summary>
    private static void LogBreakdown(ResolvedMcpServer server, List<McpToolInfo> infos,
        int groupTokens, ToolTokenBreakdown parts)
    {
        int schemaChars = server.Tools.Sum(x => ToolTokenEstimator.CompactSchema(x.JsonSchema).Length);
        Log.Debug($"MCP '{server.Config.Name}': {infos.Count} tools ~{groupTokens} tok " +
                  $"(name {parts.Name} / desc {parts.Description} / schema {parts.Schema} " +
                  $"over {schemaChars} chars = {schemaChars / (double)Math.Max(1, parts.Schema):0.0} char/tok); " +
                  $"top: {string.Join(", ", infos.OrderByDescending(x => x.EstimatedTokens).Take(3)
                      .Select(x => $"{x.Name} ~{x.EstimatedTokens}"))}");

        // 最贵的那个直接把原文头部摆出来:比例正常时,剩下的问题只可能在内容里
        AIFunction? priciest = server.Tools
            .OrderByDescending(x => ToolTokenEstimator.EstimateSchema(x.JsonSchema)).FirstOrDefault();
        if (priciest == null) return;
        string schema = ToolTokenEstimator.CompactSchema(priciest.JsonSchema);
        Log.Debug($"MCP '{server.Config.Name}' priciest schema '{priciest.Name}' ({schema.Length} chars): " +
                  schema[..Math.Min(600, schema.Length)]);
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
