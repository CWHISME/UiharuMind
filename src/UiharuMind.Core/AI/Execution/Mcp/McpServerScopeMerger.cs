/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Mcp;

/// <summary>
/// 合并后的一条 server：<b>生效的那个</b>，外加<b>被它顶掉的那个</b>（若有）。
///
/// 被顶掉的那条刻意留在结果里而不是丢弃：同名覆盖是这套设计里唯一一条会让
/// 「我明明配了却没生效」发生的规则，而它的发生频率并不低——用户很可能先在全局配过一个
/// Unity MCP，后来项目里又带一份。这条规则的代价必须在它生效的那一刻当场可见。
/// </summary>
/// <param name="Config">生效的配置</param>
/// <param name="Shadowed">被它按同名覆盖掉的全局配置；无冲突时为 null</param>
public readonly record struct EffectiveMcpServer(McpServerConfig Config, McpServerConfig? Shadowed)
{
    /// <summary>本条是否顶掉了一个同名的全局 server</summary>
    public bool ShadowsGlobal => Shadowed != null;
}

/// <summary>
/// 全局与项目级两个作用域的合并规则。
///
/// <b>纯函数，不碰单例、不读盘</b>——覆盖这类"错一次就静默走偏"的规则值得单测。
/// <see cref="McpManager"/> 只管连接与运行态，怎么并是这里的事
/// （与 <see cref="McpToolSetBuilder"/> 同一分工）。
/// </summary>
internal static class McpServerScopeMerger
{
    /// <summary>
    /// 合并两个作用域，<b>同名时项目级胜出</b>。
    ///
    /// 理由与生态一致（越靠近工作区的越具体），且用户写进项目里的通常就是要针对这个项目。
    /// 由此得到一条推论，别处都靠它：<b>合并之后，一个名字在任一时刻只对应一个生效的 server</b>——
    /// 所以角色那份 <c>DisabledMcpServers</c> 按名字存就够，不必也不该带上作用域。
    /// </summary>
    /// <param name="globalServers">全局配置（<c>McpServers.json</c>）</param>
    /// <param name="workspaceServers">项目级配置（工作区的 <c>.mcp.json</c>）</param>
    /// <returns>合并结果，项目级的排在前面（预告区与面板据此按来源分先后展示）</returns>
    public static List<EffectiveMcpServer> Merge(IReadOnlyList<McpServerConfig> globalServers,
        IReadOnlyList<McpServerConfig> workspaceServers)
    {
        if (workspaceServers.Count == 0)
        {
            return globalServers.Select(x => new EffectiveMcpServer(x, null)).ToList();
        }

        // 名字不区分大小写:与索引键、禁用名单同一口径,大小写不该成为一次静默失配
        Dictionary<string, McpServerConfig> globalByName =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (McpServerConfig server in globalServers) globalByName[server.Name] = server;

        List<EffectiveMcpServer> merged = new(workspaceServers.Count + globalServers.Count);
        HashSet<string> taken = new(StringComparer.OrdinalIgnoreCase);
        foreach (McpServerConfig server in workspaceServers)
        {
            if (!taken.Add(server.Name)) continue; //同一份文件里不可能重名(json 键唯一),防手改坏
            merged.Add(new EffectiveMcpServer(server, globalByName.GetValueOrDefault(server.Name)));
        }

        foreach (McpServerConfig server in globalServers)
        {
            if (taken.Contains(server.Name)) continue; //已被项目级顶掉,它作为 Shadowed 出现在上面
            merged.Add(new EffectiveMcpServer(server, null));
        }

        return merged;
    }
}
