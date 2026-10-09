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
/// <see cref="McpManager"/> 给扩展那一侧（<see cref="McpBridge"/>）的入口：名单签名、查找、列状态与懒连接。
///
/// 拆成 partial 文件是因为这是一个自成一体的关注点——它与连接账共享运行态与锁，
/// 但对外的入口、语义、测试面都不同。主文件只管"配置与连接账"。
/// </summary>
public partial class McpManager : IMcpServerHost
{
    /// <summary>
    /// 名单的<b>入账签名</b>：这个会话此刻会写进系统提示的 server（名字 + 说明）。
    ///
    /// 装配事实用它判断是否要重建。只含配置信息，所以连接起落不会让它变，缓存前缀因此稳定；
    /// 而外部改了项目的 <c>.mcp.json</c>、授权状态变了，这里都会变——它们本来就该让模型重新看见名单。
    /// 无副作用：不标待授权、不清连接，纯读。
    /// </summary>
    /// <param name="workspacePath">会话的工作区；空表示只有全局 server</param>
    /// <param name="disabledServers">本角色禁用的扩展名单</param>
    /// <returns>签名文本；没有可用 server 时为空串</returns>
    public string DescribeUsable(string? workspacePath, IEnumerable<string>? disabledServers)
    {
        HashSet<string> disabled = DisabledSet(disabledServers);
        return string.Join('\n', GetEffectiveServers(workspacePath)
            .Select(x => x.Config)
            .Where(x => McpServerFinder.IsUsable(x, disabled, _trust.IsTrusted))
            .Select(x => $"{x.WorkspacePath}\t{x.Name}\t{x.Description}"));
    }

    //================= IMcpServerHost（给 McpBridge） =================

    IReadOnlyList<McpServerBrief> IMcpServerHost.List(string? workspacePath, IEnumerable<string>? disabledServers)
    {
        HashSet<string> disabled = DisabledSet(disabledServers);
        return GetEffectiveServers(workspacePath)
            .Select(x => x.Config)
            .Where(x => McpServerFinder.IsUsable(x, disabled, _trust.IsTrusted))
            .Select(x => new McpServerBrief(x.Name, x.Description, GetServerStatus(x.Name, x.WorkspacePath)))
            .ToList();
    }

    McpServerLookup IMcpServerHost.Find(string? workspacePath, IEnumerable<string>? disabledServers,
        string serverName) =>
        McpServerFinder.Find(GetEffectiveServers(workspacePath).Select(x => x.Config).ToList(),
            DisabledSet(disabledServers), _trust.IsTrusted, serverName);

    Task<McpServerConnection> IMcpServerHost.ConnectAsync(McpServerConfig server, TimeSpan timeout,
        CancellationToken cancellationToken) =>
        EnsureConnectedAsync(server, timeout, forceRefresh: false, cancellationToken);

    Task<McpServerConnection> IMcpServerHost.RefreshToolsAsync(McpServerConfig server, TimeSpan timeout,
        CancellationToken cancellationToken) =>
        EnsureConnectedAsync(server, timeout, forceRefresh: true, cancellationToken);

    /// <summary>
    /// 确保连上并取回工具，最多等 <paramref name="timeout"/>。
    /// <paramref name="forceRefresh"/> 时即使已连上也重取一遍工具：server 运行中工具集可能变了
    /// （比如用户在 Unity 插件里刚启用了几个工具），而我们的快照只在连接时取过一次。
    /// </summary>
    private async Task<McpServerConnection> EnsureConnectedAsync(McpServerConfig server, TimeSpan timeout,
        bool forceRefresh, CancellationToken cancellationToken)
    {
        // 授权闸门在这条路上同样不能绕:这里是又一个会启动进程的地方
        if (!_trust.IsTrusted(server)) return NotConnected("the server has not been approved by the user");

        // 命令被改过的旧连接先作废,免得拿着旧进程当新配置用
        SyncFingerprints([server]);

        McpServerKey key = McpServerKey.Of(server);
        Task? refresh;
        lock (_lock)
        {
            McpServerRuntime runtime = GetRuntimeLocked(key);
            runtime.LastUsedUtc = DateTime.UtcNow;
            if (!forceRefresh && runtime.Tools != null && runtime.Client != null) return SnapshotLocked(runtime);

            // 模型明确要用它,无视退避:退避是防"没人问也一直重连",这里每次尝试都有人在等
            KickRefreshLocked(server, force: true);
            refresh = runtime.RefreshTask;
        }

        if (refresh is { IsCompleted: false })
        {
            try
            {
                await refresh.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return NotConnected($"still connecting after {timeout.TotalSeconds:0}s; it keeps trying in the background");
            }
        }

        lock (_lock)
        {
            return SnapshotLocked(GetRuntimeLocked(key));
        }
    }

    McpToolCatalog? IMcpServerHost.LoadCatalog(McpServerConfig server) =>
        _catalogs.Load(McpServerKey.Of(server), McpServerFingerprint.Of(server));

    /// 调用方须持有 _lock
    private static McpServerConnection SnapshotLocked(McpServerRuntime runtime)
    {
        return runtime.Tools != null && runtime.Client != null
            ? new McpServerConnection
            {
                IsConnected = true,
                Tools = runtime.Tools,
                Instructions = runtime.Instructions,
            }
            : NotConnected(runtime.Error ?? "connection failed");
    }

    private static McpServerConnection NotConnected(string reason) =>
        new() { IsConnected = false, Error = reason };
}
