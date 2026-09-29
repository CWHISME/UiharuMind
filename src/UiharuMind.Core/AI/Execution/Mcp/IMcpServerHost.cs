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
/// 按需 server 的查找结果。不是 <c>Found</c> 时，<see cref="McpServerLookup.Available"/> 用来给模型报"有哪些可用"。
/// </summary>
internal enum EMcpLookupStatus
{
    /// <summary>可用：托管开着、已授权、未被角色禁用、且是按需送达</summary>
    Found,

    /// <summary>名单里没有这个名字</summary>
    NotFound,

    /// <summary>这个 server 是直挂的，工具就在工具列表里，不该走 McpCall</summary>
    MountedDirectly,

    /// <summary>被本角色的黑名单禁用（能力层）</summary>
    DisabledByCharacter,

    /// <summary>项目级配置尚未通过安全确认</summary>
    NeedsApproval,

    /// <summary>托管开关关着（连接层）</summary>
    HostingOff,
}

/// <param name="Status">查找结论</param>
/// <param name="Server">命中时的配置</param>
/// <param name="Available">当前可用的按需 server 名，给未命中时的报错用</param>
internal sealed record McpServerLookup(
    EMcpLookupStatus Status,
    McpServerConfig? Server,
    IReadOnlyList<string> Available);

/// <summary>
/// 一次连接尝试的结果。连不上不抛异常：那是<b>一条工具结果</b>，只追加、不改前缀。
/// </summary>
internal sealed class McpServerConnection
{
    /// <summary>已连上且工具已取回</summary>
    public bool IsConnected { get; init; }

    /// <summary>工具（仅在 <see cref="IsConnected"/> 时有值）</summary>
    public IReadOnlyList<AIFunction> Tools { get; init; } = [];

    /// <summary>server 自述</summary>
    public string Instructions { get; init; } = string.Empty;

    /// <summary>没连上的原因；超时时也写在这里</summary>
    public string? Error { get; init; }
}

/// <summary>
/// 按名字找按需 server 并说清没找到的原因。纯函数：合并后的名单、角色禁用名单与授权判据都从参数进来，
/// 于是"为什么用不了"这套分支可以不碰磁盘和单例就单测。
/// </summary>
internal static class McpServerFinder
{
    /// <summary>
    /// 可用于按需调用：按需送达 + 托管开着 + 未被角色禁用 + 已授权。
    /// 装配（写进名单）与调用（查找）共用这一处，两边不会各算一遍。
    /// </summary>
    public static bool IsUsableOnDemand(McpServerConfig server, HashSet<string> disabled,
        Func<McpServerConfig, bool> isTrusted) =>
        server.IsOnDemand && McpManager.IsInPlay(server, disabled) && isTrusted(server);

    /// <summary>
    /// 查找。
    /// </summary>
    /// <param name="effective">合并后生效的配置（同名时项目级已胜出）</param>
    /// <param name="disabled">本角色禁用的 server 名（不区分大小写）</param>
    /// <param name="isTrusted">授权判据</param>
    /// <param name="serverName">模型给的名字（不区分大小写）</param>
    /// <returns>查找结果</returns>
    public static McpServerLookup Find(IReadOnlyList<McpServerConfig> effective, HashSet<string> disabled,
        Func<McpServerConfig, bool> isTrusted, string serverName)
    {
        List<string> available = effective
            .Where(x => IsUsableOnDemand(x, disabled, isTrusted)).Select(x => x.Name).ToList();

        string wanted = serverName.Trim();
        McpServerConfig? hit = effective.FirstOrDefault(x =>
            string.Equals(x.Name, wanted, StringComparison.OrdinalIgnoreCase));
        if (hit == null) return new McpServerLookup(EMcpLookupStatus.NotFound, null, available);

        EMcpLookupStatus status =
            !hit.IsEnabled ? EMcpLookupStatus.HostingOff
            : disabled.Contains(hit.Name) ? EMcpLookupStatus.DisabledByCharacter
            : !isTrusted(hit) ? EMcpLookupStatus.NeedsApproval
            : !hit.IsOnDemand ? EMcpLookupStatus.MountedDirectly
            : EMcpLookupStatus.Found;
        return new McpServerLookup(status, status == EMcpLookupStatus.Found ? hit : null, available);
    }
}

/// <summary>
/// <see cref="McpBridge"/> 需要宿主提供的几件事。抽成接口是为了让桥接逻辑
/// （查找、渲染、结果整形）不必真的拉起一个 MCP server 就能单测。
/// </summary>
internal interface IMcpServerHost
{
    /// <summary>
    /// 按名字找一个<b>按需</b> server，并把没找到的原因说清楚。
    /// 合并、授权、角色禁用的判据与装配同源，不在这里另算一遍。
    /// </summary>
    /// <param name="workspacePath">会话的工作区；空表示只有全局 server</param>
    /// <param name="disabledServers">本角色禁用的 server 名单</param>
    /// <param name="serverName">模型给的名字</param>
    /// <returns>查找结果</returns>
    McpServerLookup Find(string? workspacePath, IEnumerable<string>? disabledServers, string serverName);

    /// <summary>
    /// 确保 server 已连上并取回工具，最多等 <paramref name="timeout"/>。
    /// 超时或失败返回 <see cref="McpServerConnection.IsConnected"/> 为 false，后台连接继续。
    /// </summary>
    /// <param name="server">配置</param>
    /// <param name="timeout">最长等待</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>连接结果</returns>
    Task<McpServerConnection> ConnectAsync(McpServerConfig server, TimeSpan timeout,
        CancellationToken cancellationToken);

    /// <summary>
    /// 重新取一遍工具。server 运行中工具集可能变了，而快照只在连接时取过一次；
    /// 模型点名一个快照里没有的工具时，桥接据此重取一次再判断"真的没有"。
    /// </summary>
    /// <param name="server">配置</param>
    /// <param name="timeout">最长等待</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>最新的连接结果</returns>
    Task<McpServerConnection> RefreshToolsAsync(McpServerConfig server, TimeSpan timeout,
        CancellationToken cancellationToken);

    /// <summary>读磁盘上的工具清单缓存（server 离线时用）；没有或已过期为 null</summary>
    /// <param name="server">配置</param>
    /// <returns>清单</returns>
    McpToolCatalog? LoadCatalog(McpServerConfig server);
}
