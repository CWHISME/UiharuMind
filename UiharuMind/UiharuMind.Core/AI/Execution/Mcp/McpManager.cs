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
using ModelContextProtocol.Protocol;
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

    /// <summary>
    /// 装配前等 server 连上的上限。取得比一次普通请求还长是有意的：
    /// 等一会儿换来「这一轮真的带着工具跑」，比让第一轮静默缺工具划算得多。
    /// 而这笔等待每个进程只付一次——工具取回后就常驻缓存。
    /// </summary>
    private static readonly TimeSpan WarmupTimeout = TimeSpan.FromSeconds(10);

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
    /// 从磁盘重新读入配置（用户直接改了配置文件时用）。
    ///
    /// <b>不在此处预连</b>：那会让托管 server 的子进程随应用一起起来，而这个应用还有截图、
    /// 剪贴板、快捷问答一堆与 MCP 无关的功能。连接推迟到真正要用的那一刻，见 <see cref="WarmupAsync"/>。
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
        HashSet<string> disabled = DisabledSet(disabledServers);

        List<ResolvedMcpServer> resolved = new();
        lock (_lock)
        {
            foreach (McpServerConfig server in _servers)
            {
                if (!IsInPlay(server, disabled)) continue;

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
    /// 等托管中、还没取回工具的 server 连上（<see cref="WarmupTimeout"/> 之内），装配前调。
    ///
    /// <b>不等的话第一次装配必然赶不上</b>：<see cref="Resolve"/> 撞见还没取到工具的 server 只能
    /// 跳过它并在后台起连接。症状不是"慢一点"，而是<b>第一轮模型没有那些工具，而用户不知道为什么</b>——
    /// 后一种糟得多。工具一旦取回就常驻缓存，所以这个等待每个进程只付一次。
    ///
    /// 超时不算错：那一轮照旧不带它走，后台连接继续，下一轮自然带上，
    /// 而能力面板会把它显示成未连接。退避中（连过且失败过）的 server 不会拖住这里——
    /// 它们的刷新任务早已结束，等于不等。
    ///
    /// <b>禁用名单与 <see cref="Resolve"/> 取同一份</b>：这一轮注定挂不上的 server，
    /// 既不该为它拉起子进程，也不该为它等。分开传两份名单的话，两者迟早分叉——
    /// 曾经这里压根不看名单，于是角色明明禁掉了某个 server，第一轮发送仍要为它等满超时，
    /// 工具取回后再被 <see cref="Resolve"/> 原样丢掉。
    /// </summary>
    /// <param name="disabledServers">本角色禁用的 server 名单（能力层，见 ADR 0008）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task WarmupAsync(IEnumerable<string>? disabledServers = null,
        CancellationToken cancellationToken = default)
    {
        HashSet<string> disabled = DisabledSet(disabledServers);

        List<Task> pending = new();
        lock (_lock)
        {
            foreach (McpServerConfig server in _servers)
            {
                if (!IsInPlay(server, disabled)) continue;

                McpServerRuntime runtime = GetRuntimeLocked(server.Name);
                if (runtime.Tools != null) continue; //已经取回,不必再等

                KickRefreshLocked(server, force: false);
                // 退避中的不会有新任务,而留在运行态上的是上一次那个已完成的任务——等它等于不等。
                // 但那样 pending 非空会让预连提示白闪一帧,所以只把真正没跑完的算进来
                if (runtime.RefreshTask is { IsCompleted: false } task) pending.Add(task);
            }
        }

        if (pending.Count == 0) return;

        SetWarmingUp(true);
        try
        {
            await Task.WhenAll(pending).WaitAsync(WarmupTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Log.Warning($"MCP warmup timed out after {WarmupTimeout.TotalSeconds:0}s; " +
                        "this turn runs without the slow servers' tools");
        }
        finally
        {
            SetWarmingUp(false);
        }
    }

    /// <summary>
    /// 是否正在等 server 连上。界面据此提示——这段等待发生在用户按下发送之后，
    /// 而它可能长达十秒，不说一声就是十秒的"看着像卡死"
    /// </summary>
    public bool IsWarmingUp { get; private set; }

    /// <summary>预连状态变化。<b>可能在后台线程上触发</b>，订阅方自行切回 UI 线程</summary>
    public event Action? WarmupStateChanged;

    private void SetWarmingUp(bool value)
    {
        if (IsWarmingUp == value) return;
        IsWarmingUp = value;
        WarmupStateChanged?.Invoke();
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

    //================= 本轮该管哪些 server =================

    /// <summary>
    /// 这一轮要不要管这个 server：<b>托管</b>开着（连接层，全局一份），
    /// 且没被本角色<b>禁用</b>（能力层，按角色，见 ADR 0008）。
    ///
    /// <see cref="Resolve"/> 与 <see cref="WarmupAsync"/> 共用这一处：预连要等的与装配要挂的
    /// 必须是同一批。曾经分开写，结果 <c>WarmupAsync</c> 只看托管——角色明明禁掉的 server
    /// 照样被拉起子进程、照样等满超时，取回的工具再被 <see cref="Resolve"/> 原样丢掉。
    /// </summary>
    /// <param name="server">server 配置</param>
    /// <param name="disabled">本角色禁用的 server 名单</param>
    /// <returns>该管为 true</returns>
    internal static bool IsInPlay(McpServerConfig server, HashSet<string> disabled)
    {
        return server.IsEnabled && !disabled.Contains(server.Name);
    }

    /// 名单一律不区分大小写:server 名是用户手写进 json 的,大小写不该成为一次静默失配
    internal static HashSet<string> DisabledSet(IEnumerable<string>? names)
    {
        return names == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
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
        // 任务留在运行态上:WarmupAsync 要等的就是它。丢掉句柄的话「等它连上」无从实现
        runtime.RefreshTask = Task.Run(() => RefreshServerAsync(server, CancellationToken.None));
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
    /// <summary>
    /// 握手时报给 server 的客户端身份。<b>必须显式给</b>：不给的话 SDK 会拿当前进程的信息顶上，
    /// 于是同一个应用从桌面端连过去叫 <c>UiharuMind.Desktop</c>、从命令行连过去叫 <c>UiharuMind.CLI</c>，
    /// server 那边看到的是两个客户端。这个名字会进 server 日志，是排查问题时的第一个线索。
    /// </summary>
    private static readonly McpClientOptions ClientOptions = new()
    {
        ClientInfo = new Implementation
        {
            Name = AppInfo.Name,
            Version = AppInfo.Version.ToString(),
        },
    };

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
            .CreateAsync(CreateTransport(server), ClientOptions, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

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

        /// 正在跑的那次刷新;WarmupAsync 据此等待。失败过的那次是已完成任务,等于不等
        public Task? RefreshTask { get; set; }

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
