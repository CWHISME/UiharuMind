/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/


using Microsoft.Agents.AI.Mcp;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using UiharuMind.Core.AI.Execution.Tools;

using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.AI.Execution.Mcp;

/// <summary>
/// MCP server 的连接与工具集管理。配置持久化为生态标准的 <c>mcpServers</c> 形状；
/// 托管中的 server 经官方 SDK 连接，工具转成 <c>AIFunction</c> 汇入 agent（统一走审批管道）。
///
/// <b>常驻缓存 + 修订号</b>：装配同步取缓存，绝不等网络——慢启动或挂死的 server 不会卡住会话挂接；
/// 后台取回工具后修订号自增，装配快照据此在下次挂接时重建。
///
/// <b>失败按指数退避</b>：连不上的 server 曾经在每次装配时被重新拉起一遍（缓存永远为空，
/// 于是每次都触发后台补连），既磨进程又让用户只感到"卡"。现在失败会留下时间戳与错误原文，
/// 前者管住重试节奏，后者摆到 UI 上——"为什么没工具"必须能当场看见。
/// </summary>
public class McpManager : Singleton<McpManager>, IInitialize
{
    /// <summary>标准形状的配置文件,可与别的 MCP 客户端互拷</summary>
    private const string ConfigFileName = "McpServers.json";

    /// <summary>本项目特有的两项(是否托管、是否注入自述),不进标准文件</summary>
    private const string StateFileName = "McpServerStates.json";

    /// <summary>标准配置文件的完整路径。设置页展示它,用户可直接编辑或整段替换</summary>
    public static string ConfigFilePath => SaveUtility.GetSaveDataPath(ConfigFileName);

    /// 连续失败第 n 次之后的等待时长,末项为封顶值
    private static readonly TimeSpan[] RetryBackoff =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromMinutes(5),
    ];

    private readonly object _lock = new(); //守护配置列表、运行态与修订号,全场只此一把
    private readonly List<McpServerConfig> _servers = new();
    private readonly Dictionary<string, McpServerRuntime> _runtimes = new(StringComparer.OrdinalIgnoreCase);
    private int _revision; //工具集修订号

    /// <summary>
    /// 工具集修订号：配置增删改与后台取回工具都会使其自增，
    /// 装配快照据此感知 MCP 侧的变化（工具集与 server 自述都算在内）。
    /// </summary>
    public int Revision
    {
        get
        {
            lock (_lock)
            {
                return _revision;
            }
        }
    }

    public void OnInitialize()
    {
        Reload();
    }

    /// <summary>
    /// 从磁盘重新读入配置（用户直接改了配置文件时用）
    /// </summary>
    public void Reload()
    {
        McpServersFile file = SaveUtility.LoadRootFile<McpServersFile>(ConfigFileName) ?? new McpServersFile();
        Dictionary<string, McpServerLocalState> states =
            SaveUtility.LoadRootFile<Dictionary<string, McpServerLocalState>>(StateFileName) ?? new();

        List<McpServerConfig> configs = file.ToConfigs(states);
        List<McpClient> orphans;
        lock (_lock)
        {
            _servers.Clear();
            _servers.AddRange(configs);
            // 整份配置换掉,旧连接一律作废;拿到锁外释放,子进程不能就这么留着
            orphans = _runtimes.Values.Where(x => x.Client != null).Select(x => x.Client!).ToList();
            _runtimes.Clear();
            _revision++;
        }

        foreach (McpClient client in orphans) _ = client.DisposeAsync();
    }

    /// <summary>
    /// 获取全部 server 配置
    /// </summary>
    /// <returns>配置列表</returns>
    public List<McpServerConfig> GetServers()
    {
        lock (_lock)
        {
            return new List<McpServerConfig>(_servers);
        }
    }

    /// <summary>
    /// 获取 server 的运行状态
    /// </summary>
    /// <param name="name">server 名</param>
    /// <returns>状态快照</returns>
    public McpServerStatus GetServerStatus(string name)
    {
        lock (_lock)
        {
            if (!_runtimes.TryGetValue(name, out McpServerRuntime? runtime))
            {
                return new McpServerStatus { State = EMcpConnectionState.Disconnected };
            }

            return new McpServerStatus
            {
                State = runtime.State,
                ToolCount = runtime.Tools?.Count ?? 0,
                EstimatedTokens = runtime.EstimatedTokens,
                Error = runtime.Error,
                HasInstructions = runtime.Instructions.Length > 0,
            };
        }
    }

    /// <summary>
    /// 新增或更新 server 配置；缓存失效、断开旧连接，托管中的在后台重连
    /// </summary>
    /// <param name="server">配置</param>
    public void SaveServer(McpServerConfig server)
    {
        lock (_lock)
        {
            int index = _servers.FindIndex(x => string.Equals(x.Name, server.Name, StringComparison.Ordinal));
            if (index >= 0) _servers[index] = server;
            else _servers.Add(server);
            _revision++; //配置变化(含启停与自述开关)即视为 MCP 侧变化
        }

        Save();
        DropRuntime(server.Name);
    }

    /// <summary>
    /// 删除 server 配置
    /// </summary>
    /// <param name="name">server 名</param>
    public void DeleteServer(string name)
    {
        lock (_lock)
        {
            _servers.RemoveAll(x => string.Equals(x.Name, name, StringComparison.Ordinal));
            _revision++;
        }

        Save();
        DropRuntime(name);
    }

    /// <summary>
    /// 解析出一次装配要用的 MCP 工具集与自述。<b>同步取缓存，绝不等待网络</b>；
    /// 尚未取回工具的 server 在后台补连（受退避约束），完成后修订号自增，
    /// 下一次挂接经装配快照差异自动重建。
    /// </summary>
    /// <param name="disabledServers">本角色禁用的 server 名单（能力层，见 ADR 0007）</param>
    /// <returns>工具集、分组明细与自述</returns>
    public McpToolSet Resolve(IEnumerable<string>? disabledServers = null)
    {
        HashSet<string> disabled = disabledServers == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(disabledServers, StringComparer.OrdinalIgnoreCase);

        List<ResolvedMcpServer> resolved = new();
        lock (_lock)
        {
            foreach (McpServerConfig server in _servers)
            {
                if (!server.IsEnabled || disabled.Contains(server.Name)) continue;

                McpServerRuntime runtime = GetRuntimeLocked(server.Name);
                if (runtime.Tools == null)
                {
                    KickRefreshLocked(server, force: false);
                    continue;
                }

                resolved.Add(new ResolvedMcpServer(server, runtime.Tools, runtime.Instructions));
            }
        }

        return McpToolSetBuilder.Build(resolved);
    }

    /// <summary>
    /// 连上<b>指定的一个</b> server 并取回工具，等待完成（设置页的测试连接）。
    ///
    /// 刻意<b>不看托管开关</b>：新建的 server 默认不托管，而"填完配置就点测试"是最自然的顺序，
    /// 若跟着"刷新全部已托管的"走，这里会一声不响什么都不做。
    /// 手动测试也意味着用户刚动过配置或环境，因此退避账从头算起。
    /// </summary>
    /// <param name="name">server 名</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task TestServerAsync(string name, CancellationToken cancellationToken = default)
    {
        McpServerConfig? server;
        lock (_lock)
        {
            server = _servers.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.Ordinal));
            if (server == null) return;

            McpServerRuntime runtime = GetRuntimeLocked(name);
            runtime.FailureCount = 0;
            runtime.NextAttemptUtc = DateTime.MinValue;
        }

        await RefreshServerAsync(server, cancellationToken).ConfigureAwait(false);
    }

    //================= 连接与刷新 =================

    /// <summary>调用方须持有 _lock</summary>
    private McpServerRuntime GetRuntimeLocked(string name)
    {
        if (_runtimes.TryGetValue(name, out McpServerRuntime? runtime)) return runtime;
        runtime = new McpServerRuntime();
        _runtimes[name] = runtime;
        return runtime;
    }

    /// <summary>调用方须持有 _lock。受退避约束，force 时无视退避</summary>
    private void KickRefreshLocked(McpServerConfig server, bool force)
    {
        McpServerRuntime runtime = GetRuntimeLocked(server.Name);
        if (runtime.Refreshing) return;
        if (!force && DateTime.UtcNow < runtime.NextAttemptUtc) return;

        runtime.Refreshing = true;
        _ = Task.Run(() => RefreshServerAsync(server, CancellationToken.None));
    }

    private async Task RefreshServerAsync(McpServerConfig server, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            //手动路径进来时标记还没置上;后台路径由 KickRefreshLocked 置好,这里是幂等补一次
            GetRuntimeLocked(server.Name).Refreshing = true;
        }

        try
        {
            McpClient client = await ConnectAsync(server, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<AIFunction> tools = await client
                .ListAgentToolsWithTaskSupportAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            lock (_lock)
            {
                McpServerRuntime runtime = GetRuntimeLocked(server.Name);
                runtime.Tools = tools;
                // 取回时就地算好:分词有代价,而状态是设置页与角色编辑页在 UI 线程上问的
                runtime.EstimatedTokens = ToolTokenEstimator.Estimate(tools);
                // server 自述:MCP initialize 响应里 server 用来说明"怎么用我这套工具"的官方口子。
                // 丢掉它等于让模型只看见工具签名、看不见用法
                runtime.Instructions = client.ServerInstructions?.Trim() ?? string.Empty;
                runtime.State = EMcpConnectionState.Connected;
                runtime.Error = null;
                runtime.FailureCount = 0;
                runtime.NextAttemptUtc = DateTime.MinValue;
                _revision++;
            }
        }
        catch (Exception e)
        {
            Log.Warning($"MCP server '{server.Name}' unavailable: {e.Message}");
            lock (_lock)
            {
                McpServerRuntime runtime = GetRuntimeLocked(server.Name);
                runtime.State = EMcpConnectionState.Failed;
                runtime.Error = e.Message;
                runtime.FailureCount++;
                TimeSpan wait = RetryBackoff[Math.Min(runtime.FailureCount - 1, RetryBackoff.Length - 1)];
                runtime.NextAttemptUtc = DateTime.UtcNow + wait;
            }
        }
        finally
        {
            lock (_lock)
            {
                GetRuntimeLocked(server.Name).Refreshing = false;
            }
        }
    }

    /// 连接本身要 await,不能在锁里做;同一 server 的并发由 Refreshing 标记挡在门外
    private async Task<McpClient> ConnectAsync(McpServerConfig server, CancellationToken cancellationToken)
    {
        McpClient? existing;
        lock (_lock)
        {
            McpServerRuntime runtime = GetRuntimeLocked(server.Name);
            existing = runtime.Client;
            if (existing == null) runtime.State = EMcpConnectionState.Connecting;
        }

        if (existing != null) return existing;

        McpClient client = await McpClient
            .CreateAsync(CreateTransport(server), cancellationToken: cancellationToken).ConfigureAwait(false);

        McpClient? raced = null;
        lock (_lock)
        {
            McpServerRuntime runtime = GetRuntimeLocked(server.Name);
            // 后台补连与手动测试可能同时进到这里,谁先落谁算数,输的那个连接必须收掉
            if (runtime.Client != null) raced = client;
            else runtime.Client = client;
            client = runtime.Client;
        }

        if (raced != null) _ = raced.DisposeAsync();
        return client;
    }

    private static IClientTransport CreateTransport(McpServerConfig server)
    {
        if (server.TransportType == EMcpTransportType.Http)
        {
            return new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(server.Url),
                Name = server.Name,
                AdditionalHeaders = server.Headers.Count > 0
                    ? new Dictionary<string, string>(server.Headers)
                    : null,
            });
        }

        return new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = server.Name,
            Command = server.Command,
            // 逐项存放,不再 Split(' '):含空格的路径与带引号的参数曾在这里被静默拆坏
            Arguments = server.Args.Count > 0 ? server.Args.ToArray() : null,
            EnvironmentVariables = server.EnvironmentVariables.Count > 0
                ? server.EnvironmentVariables.ToDictionary(x => x.Key, string? (x) => x.Value)
                : null,
        });
    }

    /// <summary>
    /// 丢弃一个 server 的运行态并断开连接，顺带清掉<b>已不在配置里的那些</b>。
    ///
    /// 后半句是必须的：改名保存时，新名字这条会被重建，而旧名字那条既不会再被任何人问到，
    /// 也不会自己退出——stdio 的话那是一个留在后台的子进程。
    /// </summary>
    /// <param name="name">要重建的 server 名</param>
    private void DropRuntime(string name)
    {
        List<McpClient> orphans = new();
        lock (_lock)
        {
            HashSet<string> alive = _servers.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string key in _runtimes.Keys.ToList())
            {
                if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase) && alive.Contains(key)) continue;
                if (!_runtimes.Remove(key, out McpServerRuntime? runtime)) continue;
                if (runtime.Client != null) orphans.Add(runtime.Client);
            }
        }

        foreach (McpClient client in orphans) _ = client.DisposeAsync();
    }

    private void Save()
    {
        List<McpServerConfig> snapshot;
        lock (_lock)
        {
            snapshot = new List<McpServerConfig>(_servers);
        }

        var (standard, states) = McpServersFile.FromConfigs(snapshot);
        SaveUtility.SaveRootFile(ConfigFileName, standard);
        SaveUtility.SaveRootFile(StateFileName, states);
    }

    /// <summary>一个 server 的运行态。只在 <c>_lock</c> 内读写</summary>
    private sealed class McpServerRuntime
    {
        public McpClient? Client { get; set; }
        public EMcpConnectionState State { get; set; } = EMcpConnectionState.Disconnected;
        public IReadOnlyList<AIFunction>? Tools { get; set; }
        public int? EstimatedTokens { get; set; }
        public string Instructions { get; set; } = string.Empty;
        public string? Error { get; set; }
        public bool Refreshing { get; set; }
        public int FailureCount { get; set; }
        public DateTime NextAttemptUtc { get; set; }
    }

}

/// <summary>
/// 一个 server 的状态快照（UI 展示用）
/// </summary>
public sealed class McpServerStatus
{
    /// <summary>连接状态</summary>
    public EMcpConnectionState State { get; init; }

    /// <summary>已取回的工具数</summary>
    public int ToolCount { get; init; }

    /// <summary>工具定义的估算 token 数；尚未取回工具时为 null（那是「不知道」，不是 0）</summary>
    public int? EstimatedTokens { get; init; }

    /// <summary>失败原因；成功或未连接时为 null</summary>
    public string? Error { get; init; }

    /// <summary>server 是否给了自述</summary>
    public bool HasInstructions { get; init; }
}
