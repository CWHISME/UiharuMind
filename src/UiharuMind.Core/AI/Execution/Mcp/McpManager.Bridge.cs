/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Mcp;

using ModelContextProtocol.Client;

/// <summary>
/// <see cref="McpManager"/> 给扩展那一侧（<see cref="McpBridge"/>）的入口：列状态、查找与懒连接。
///
/// 拆成 partial 文件是因为这是一个自成一体的关注点——它与连接账共享运行态与锁，
/// 但对外的入口、语义、测试面都不同。主文件只管"配置与连接账"。
/// </summary>
public partial class McpManager : IMcpServerHost
{
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

    IReadOnlyList<McpServerBrief> IMcpServerHost.Overview(string? workspacePath,
        IEnumerable<string>? disabledServers)
    {
        HashSet<string> disabled = DisabledSet(disabledServers);
        List<McpServerBrief> all = new();
        foreach (EffectiveMcpServer effective in GetEffectiveServers(workspacePath))
        {
            // 被项目级同名配置顶掉的那条不列：它不是"另一台"，用户看到的同一台的两份写法
            McpServerConfig server = effective.Config;
            EMcpLookupStatus blocked = McpServerFinder.BlockedBy(server, disabled, _trust.IsTrusted);
            all.Add(new McpServerBrief(server.Name, server.Description,
                GetServerStatus(server.Name, server.WorkspacePath))
            {
                Blocked = blocked == EMcpLookupStatus.Found ? null : blocked,
            });
        }

        return all;
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

    Task<McpServerConnection> IMcpServerHost.ReconnectAsync(McpServerConfig server, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        // 先丢再连:死的 client 挂在运行态上,EnsureConnectedAsync 会认账(见下)
        DropDeadConnection(server);
        return EnsureConnectedAsync(server, timeout, forceRefresh: true, cancellationToken);
    }

    /// <summary>连接被判定死亡:client 置空收掉(工具快照保留——server 离线时帮助还读它),下次连接不再认账</summary>
    private void DropDeadConnection(McpServerConfig server)
    {
        McpClient? dead = null;
        lock (_lock)
        {
            if (_runtimes.TryGetValue(McpServerKey.Of(server), out McpServerRuntime? runtime))
            {
                dead = runtime.Client;
                runtime.Client = null;
            }
        }

        if (dead != null) _ = dead.DisposeAsync();
    }

    /// <summary>
    /// 确保连上并取回工具，最多等 <paramref name="timeout"/>。
    /// <paramref name="forceRefresh"/> 时即使已连上也重取一遍工具：server 运行中工具集可能变了
    /// （比如用户在 Unity 插件里刚启用了几个工具），而我们的快照只在连接时取过一次。
    /// </summary>
    private async Task<McpServerConnection> EnsureConnectedAsync(McpServerConfig server, TimeSpan timeout,
        bool forceRefresh, CancellationToken cancellationToken)
    {
        // 授权闸门在这条路上同样不能绕:这里是又一个会启动进程的地方
        if (!_trust.IsTrusted(server)) return NotConnected("该 server 尚未经用户授权");

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
                return NotConnected($"已等待 {timeout.TotalSeconds:0} 秒仍在连接；它还在后台继续重连。" +
                                    "请稍等片刻后原样重试这次调用——不要改用你自己的脚本绕过它");
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
            : NotConnected(runtime.Error ?? "连接失败");
    }

    private static McpServerConnection NotConnected(string reason) =>
        new() { IsConnected = false, Error = reason };
}
