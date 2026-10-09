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
using UiharuMind.Core.AI.Execution.Files;

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
public partial class McpManager : Singleton<McpManager>, IInitialize
{
    /// <summary>标准配置文件的完整路径。设置页展示它,用户可直接编辑或整段替换</summary>
    public static string ConfigFilePath => AppPaths.Config.McpServers;

    /// <summary>
    /// 零租约之后连接留存多久。
    ///
    /// <b>为什么不是「会话切走就断」</b>：工作区是<b>会话级</b>字段（<c>ChatSession.WorkspacePath</c>），
    /// 不是应用级状态——不同会话可以同时挂在不同工作区上，而定时任务更会在无头会话里跑。
    /// 照「切走即断」实现会掐掉正在后台跑的那一轮。
    ///
    /// <b>也不是永不回收</b>：stdio server 意味着常驻子进程，试过五个项目就是五窝进程挂到应用退出。
    ///
    /// 取一小时是因为在项目之间来回切是常态，而重连要让模型的下一次调用多等几秒。
    /// </summary>
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(1);

    /// 回收的检查间隔。比 IdleTimeout 小一个量级就够——早晚几分钟无所谓,检查太密纯属空转
    private static readonly TimeSpan ReclaimInterval = TimeSpan.FromMinutes(5);

    /// 连续失败第 n 次之后的等待时长,末项为封顶值
    private static readonly TimeSpan[] RetryBackoff =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromMinutes(5),
    ];

    private readonly object _lock = new(); //守护配置列表、运行态与修订号,全场只此一把
    private readonly List<McpServerConfig> _servers = new(); //全局作用域那一份(McpServers.json)
    private readonly Dictionary<McpServerKey, McpServerRuntime> _runtimes = new(); //按(作用域,名字)索引
    private readonly McpTrustStore _trust = new(); //项目级配置的安全授权账本,自带锁
    private readonly McpToolCatalogStore _catalogs = new(); //工具清单磁盘缓存(server 离线时帮助读它)
    private readonly Dictionary<string, int> _leases = new(StringComparer.Ordinal); //在途租约,键同索引键的作用域那一半
    private CancellationTokenSource? _reclaimCancellation; //空闲回收循环,首次连上时惰性启动
    private int _revision; //配置修订号

    /// <summary>
    /// 配置修订号：增删改、托管开关、授权变化都会使其自增。
    ///
    /// <b>连接事件不计入</b>：MCP 工具不进 <c>tools</c>（ADR 0079），连上、断开、重连都不该改前缀。
    /// 系统提示里那行名单的变化由 <see cref="DescribeUsable"/> 单独入账。
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
    /// <b>不在此处连接</b>：那会让托管 server 的子进程随应用一起起来，而这个应用还有截图、
    /// 剪贴板、快捷问答一堆与 MCP 无关的功能。连接推迟到模型第一次查或调它的那一刻。
    /// </summary>
    public void Reload()
    {
        _trust.Reload();
        McpServersFile file = SaveUtility.Load<McpServersFile>(AppPaths.Config.McpServers) ?? new McpServersFile();
        Dictionary<string, McpServerLocalState> states =
            SaveUtility.Load<Dictionary<string, McpServerLocalState>>(AppPaths.Config.McpServerStates) ?? new();

        List<McpServerConfig> configs = file.ToConfigs(states);
        List<McpClient> orphans = new();
        lock (_lock)
        {
            _servers.Clear();
            _servers.AddRange(configs);
            // 全局那份配置换掉,它的连接一律作废;拿到锁外释放,子进程不能就这么留着。
            // 项目级的运行态不动:它们的配置来自各自工作区的 .mcp.json,与这份文件无关,
            // 一并清掉会把别的会话正在用的连接顺手掐死
            foreach (McpServerKey key in _runtimes.Keys.Where(x => x.IsGlobal).ToList())
            {
                if (!_runtimes.Remove(key, out McpServerRuntime? runtime)) continue;
                if (runtime.Client != null) orphans.Add(runtime.Client);
            }

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
    /// <param name="workspacePath">项目级 server 的工作区路径；全局 server 传空</param>
    /// <returns>状态快照</returns>
    public McpServerStatus GetServerStatus(string name, string? workspacePath = null)
    {
        lock (_lock)
        {
            if (!_runtimes.TryGetValue(new McpServerKey(workspacePath, name), out McpServerRuntime? runtime))
            {
                return new McpServerStatus { State = EMcpConnectionState.Disconnected };
            }

            return new McpServerStatus
            {
                State = runtime.State,
                ToolCount = runtime.Tools?.Count ?? 0,
                LastToolCount = runtime.LastToolCount,
                EstimatedTokens = runtime.EstimatedTokens,
                Error = runtime.Error,
            };
        }
    }

    /// <summary>
    /// 新增或更新<b>全局</b> server 配置；缓存失效、断开旧连接，托管中的在后台重连。
    ///
    /// 只管全局那一份：项目级配置的唯一来源是工作区里的 <c>.mcp.json</c>，
    /// 由用户直接编辑那个文件，本应用不写它（那是要入库共享的文件）。
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
        DropRuntime(new McpServerKey(null, server.Name));
    }

    /// <summary>
    /// 删除<b>全局</b> server 配置
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
        DropRuntime(new McpServerKey(null, name));
    }

    /// <summary>
    /// 一次装配从 MCP 侧拿的东西：这一轮可用的 server 名单，与查、调它们的桥接。
    /// <b>不连接、不等网络</b>：MCP 工具不进 <c>tools</c>（ADR 0079），模型第一次查或调某个 server 时才连
    /// </summary>
    /// <param name="workspacePath">
    /// 本次装配的工作区；项目级 <c>.mcp.json</c> 里的 server 据此并入（同名覆盖全局）。
    /// 空表示未绑定工作区，只有全局 server 参与。
    /// </param>
    /// <param name="disabledServers">本角色禁用的扩展名单（能力层，见 ADR 0008）</param>
    /// <param name="spillDirectory">超限结果的落盘目录（会话自己的产出房间）；无会话时为空，退到缓存下的草稿目录</param>
    /// <returns>名单与桥接</returns>
    public McpToolSet Resolve(string? workspacePath = null, IEnumerable<string>? disabledServers = null,
        string spillDirectory = "")
    {
        List<string> disabledList = disabledServers?.ToList() ?? [];
        List<McpServerConfig> inPlay = InPlayServers(workspacePath, DisabledSet(disabledList));
        if (inPlay.Count == 0) return McpToolSet.Empty;

        string spill = spillDirectory.Length > 0 ? spillDirectory : AppPaths.Cache.Scratch;
        // 落进会话房间时图片路径写成草稿目录简写,模型拿它直接喂 Read / 生图;退到缓存时没有简写可用
        AgentPathResolver? paths = spillDirectory.Length > 0 ? new AgentPathResolver(workspacePath, spillDirectory) : null;
        return new McpToolSet
        {
            Servers = inPlay.Select(x => new McpRosterServer
            {
                Name = x.Name,
                WorkspacePath = x.WorkspacePath,
                Description = x.Description,
            }).ToList(),
            Bridge = new McpBridge(this, workspacePath, disabledList, spill, paths),
        };
    }

    /// <summary>
    /// 这个 MCP 工具有没有自己声明只读（<c>readOnlyHint</c>）。只认连上时取回的那份注解：
    /// 没连过的 server 答不了，按「不是」算——第一次调用照常问用户（ADR 0079）。
    /// server 按发起调用的会话的工作区认（项目级同名胜出）：全局那个 fs 声明的只读，不能拿来放行项目里另一个 fs
    /// </summary>
    /// <param name="serverName">server 名</param>
    /// <param name="toolName">工具名（与声明逐字相同才算）</param>
    /// <param name="workspacePath">发起调用的会话的工作区；空表示只有全局 server</param>
    /// <returns>声明了只读为 true</returns>
    public bool IsReadOnlyTool(string serverName, string toolName, string? workspacePath)
    {
        McpServerConfig? server = GetEffectiveServers(workspacePath).Select(x => x.Config)
            .FirstOrDefault(x => string.Equals(x.Name, serverName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (server == null) return false;
        lock (_lock)
        {
            return _runtimes.TryGetValue(McpServerKey.Of(server), out McpServerRuntime? runtime) &&
                   runtime.ReadOnlyTools?.Contains(toolName) == true;
        }
    }


    /// <summary>
    /// 连上<b>指定的一个</b> server 并取回工具，等待完成（设置页的测试连接）。
    ///
    /// 刻意<b>不看托管开关</b>：新建的 server 默认不托管，而"填完配置就点测试"是最自然的顺序，
    /// 若跟着"刷新全部已托管的"走，这里会一声不响什么都不做。
    /// 手动测试也意味着用户刚动过配置或环境，因此退避账从头算起。
    /// </summary>
    /// <param name="name">server 名</param>
    /// <param name="workspacePath">项目级 server 的工作区路径；全局 server 传空</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task TestServerAsync(string name, string? workspacePath = null,
        CancellationToken cancellationToken = default)
    {
        McpServerConfig? server = GetEffectiveServers(workspacePath)
            .Select(x => x.Config)
            .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.Ordinal));
        if (server == null) return;

        // 授权闸门在测试连接这条路上同样不能绕:「点一下测试」不构成对入库命令的授权
        if (!_trust.IsTrusted(server))
        {
            MarkPendingApproval([McpServerKey.Of(server)]);
            return;
        }

        lock (_lock)
        {
            McpServerRuntime runtime = GetRuntimeLocked(McpServerKey.Of(server));
            runtime.FailureCount = 0;
            runtime.NextAttemptUtc = DateTime.MinValue;
        }

        await RefreshServerAsync(server, cancellationToken).ConfigureAwait(false);
    }

    //================= 本轮该管哪些 server =================

    /// <summary>
    /// 本次装配<b>实际生效</b>的那份合并名单：全局配置并上该工作区 <c>.mcp.json</c> 的，
    /// 同名时项目级胜出，被顶掉的那条作为 <c>Shadowed</c> 一并带回。
    ///
    /// 合并只在这一处做——装配、查找、能力面板几条路都从这里取，不各自算一遍。
    /// <b>要读盘，因此在锁外</b>（读的是工作区里的小 json，与装配读 AGENTS.md 同量级）。
    /// </summary>
    /// <param name="workspacePath">工作区路径；空表示只有全局作用域</param>
    /// <returns>合并结果，项目级在前</returns>
    public List<EffectiveMcpServer> GetEffectiveServers(string? workspacePath)
    {
        List<McpServerConfig> workspaceServers = McpWorkspaceConfigLoader.Load(workspacePath);
        List<McpServerConfig> globalServers;
        lock (_lock)
        {
            globalServers = new List<McpServerConfig>(_servers);
        }

        return McpServerScopeMerger.Merge(globalServers, workspaceServers);
    }

    /// <summary>
    /// 这一轮真正参与的那批：合并 → 托管与能力过滤 → <b>授权闸门</b>。
    ///
    /// 闸门钉在这里而不是调用方，是因为这是<b>唯一会启动进程的地方</b>。
    /// 若配置由外部传进来、授权由外部先查，那条不变式就依赖「每个调用点都记得查一次」，
    /// 而那是最典型的会被下一个调用点忘掉的约定。
    ///
    /// 未获授权的不算失败，标成 <see cref="EMcpConnectionState.PendingApproval"/>——
    /// 缺的是用户那一下授权，不是连接能力，而面板必须能显示「有东西因为没授权而没挂上」。
    /// </summary>
    /// <param name="workspacePath">工作区路径</param>
    /// <param name="disabled">本角色禁用的 server 名单</param>
    /// <returns>可以连、也该连的那批配置</returns>
    private List<McpServerConfig> InPlayServers(string? workspacePath, HashSet<string> disabled)
    {
        List<McpServerConfig> result = new();
        List<McpServerKey> pending = new();
        foreach (EffectiveMcpServer effective in GetEffectiveServers(workspacePath))
        {
            McpServerConfig server = effective.Config;
            if (!IsInPlay(server, disabled)) continue;

            if (!_trust.IsTrusted(server))
            {
                pending.Add(McpServerKey.Of(server));
                continue;
            }

            result.Add(server);
        }

        if (pending.Count > 0) MarkPendingApproval(pending);
        // 命令被改过的那些在这里就地作废,免得下面拿着旧连接当新配置用
        SyncFingerprints(result);
        return result;
    }

    /// 只动状态,不碰工具缓存:曾授权过、命令刚被改掉的那条,旧工具还能用到下一次重连
    private void MarkPendingApproval(List<McpServerKey> keys)
    {
        lock (_lock)
        {
            foreach (McpServerKey key in keys)
            {
                McpServerRuntime runtime = GetRuntimeLocked(key);
                if (runtime.State == EMcpConnectionState.PendingApproval) continue;
                runtime.State = EMcpConnectionState.PendingApproval;
                runtime.Error = null;
            }
        }
    }

    //================= 租约与空闲回收 =================

    /// <summary>
    /// 取一份连接租约，<b>持有期间该作用域的连接不会被空闲回收</b>。一轮开跑时取，一轮结束时还
    /// （见 <c>TurnDriver.RunAsync</c>，与 <c>SessionManager.Running.BeginRun</c> 同形同寿）。
    ///
    /// <b>为什么是租约而不是「会话关了就断」</b>：要拦的是「现在还有没有人在用」，
    /// 而唯一能诚实回答它的信息就是「有没有一轮正在跑」，别的都是猜。
    /// 定时任务的无头轮次同样走 <c>TurnDriver</c>，因此天然被覆盖——不会再出现
    /// 「人在 A 项目看着屏幕，B 项目的后台任务被掐掉连接」。
    ///
    /// <b>按作用域而非按 server 计数</b>：租约回答的是「这个工作区在被用吗」，
    /// 逐 server 计数要先知道这一轮会挂哪些 server，而那取决于角色黑名单——
    /// 于是租约的粒度会依赖一份随时可改的配置，得不偿失。
    /// 全局作用域的连接被所有会话共用，<b>任何</b>在途轮次都算它的租约。
    /// </summary>
    /// <param name="workspacePath">本轮会话的工作区；空表示未绑定（只占全局那一份租约）</param>
    /// <returns>释放即归还的租约；重复释放安全</returns>
    public IDisposable AcquireLease(string? workspacePath)
    {
        string workspace = McpServerKey.NormalizeWorkspace(workspacePath);
        DateTime now = DateTime.UtcNow;
        lock (_lock)
        {
            _leases[workspace] = _leases.GetValueOrDefault(workspace) + 1;
            // 取租约就算一次活动:一轮跑得比 IdleTimeout 还久时,归还的瞬间不该已经"超时一小时"
            TouchLocked(workspace, now);
        }

        return new McpLease(this, workspace);
    }

    private void ReleaseLease(string workspace)
    {
        DateTime now = DateTime.UtcNow;
        lock (_lock)
        {
            int remaining = _leases.GetValueOrDefault(workspace) - 1;
            if (remaining <= 0) _leases.Remove(workspace);
            else _leases[workspace] = remaining;

            // 归还的这一刻才是空闲计时的起点
            TouchLocked(workspace, now);
        }
    }

    /// 刷新该作用域下各连接的"最后使用"时刻。调用方须持有 _lock。
    /// 工作区租约同时刷新全局那一批:那一轮同样在用全局 server
    private void TouchLocked(string workspace, DateTime now)
    {
        foreach ((McpServerKey key, McpServerRuntime runtime) in _runtimes)
        {
            if (key.IsGlobal || string.Equals(key.Workspace, workspace, StringComparison.Ordinal))
            {
                runtime.LastUsedUtc = now;
            }
        }
    }

    /// 首次连上时才启动回收循环:这个应用还有截图、剪贴板一堆与 MCP 无关的功能,
    /// 从没连过 server 的进程不该为它空转
    private void EnsureReclaimLoop()
    {
        CancellationTokenSource cancellation;
        lock (_lock)
        {
            if (_reclaimCancellation != null) return;
            _reclaimCancellation = cancellation = new CancellationTokenSource();
        }

        _ = ReclaimLoopAsync(cancellation.Token);
    }

    private async Task ReclaimLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(ReclaimInterval, cancellationToken).ConfigureAwait(false);
                ReclaimIdle();
            }
        }
        catch (OperationCanceledException)
        {
            //应用退出,正常收尾
        }
        catch (Exception e)
        {
            Log.Warning($"MCP idle reclaim loop stopped: {e.Message}");
        }
    }

    /// <summary>
    /// 断开零租约且已空闲超过 <see cref="IdleTimeout"/> 的连接。
    ///
    /// <b>工具缓存跟着一起丢</b>：那批 <c>AIFunction</c> 持有 <c>McpClient</c>，
    /// client 一释放它们就是「定义还在但调不动」——挂上去的工具必须是能调的。
    /// 但 <see cref="McpServerRuntime.LastToolCount"/> 与估算 token 留着，
    /// 否则能力面板会在回收后突然显示「0 个工具」，用户会以为坏了。
    /// </summary>
    internal void ReclaimIdle()
    {
        DateTime now = DateTime.UtcNow;
        List<McpClient> orphans = new();
        lock (_lock)
        {
            int totalLeases = _leases.Values.Sum();
            foreach ((McpServerKey key, McpServerRuntime runtime) in _runtimes)
            {
                if (runtime.Client == null) continue; //没连上的没有可回收的资源
                if (runtime.Refreshing) continue; //正在连,别把它连出来的东西掐掉

                bool leased = key.IsGlobal
                    ? totalLeases > 0
                    : _leases.GetValueOrDefault(key.Workspace) > 0;
                if (leased || now - runtime.LastUsedUtc < IdleTimeout) continue;

                orphans.Add(runtime.Client);
                runtime.Client = null;
                runtime.Tools = null; //连接没了,工具就调不动了——留着等于给模型一批坏工具
                runtime.Instructions = string.Empty;
                runtime.State = EMcpConnectionState.Disconnected;
                Log.Debug($"MCP '{key}' reclaimed after {IdleTimeout.TotalMinutes:0} min idle");
            }
        }

        foreach (McpClient client in orphans) _ = client.DisposeAsync();
    }

    /// <summary>
    /// 配置指纹变了就丢掉旧运行态：命令改过之后，旧连接连着的还是<b>按旧命令起的那个子进程</b>。
    /// <see cref="DropRuntime"/> 里「已不在配置里就清掉」那条规则命中不了它——它还在配置里，只是变了。
    ///
    /// 这一处与安全确认共用同一份指纹，于是<b>「授权还没给、进程已经按新命令起来了」不可能发生</b>：
    /// 指纹变 ⇒ 授权失效 ⇒ 连接同时被丢，两件事是同一个判据的两个后果。
    /// </summary>
    /// <param name="servers">本轮参与的配置</param>
    private void SyncFingerprints(List<McpServerConfig> servers)
    {
        List<McpClient> orphans = new();
        lock (_lock)
        {
            foreach (McpServerConfig server in servers)
            {
                McpServerKey key = McpServerKey.Of(server);
                if (!_runtimes.TryGetValue(key, out McpServerRuntime? runtime)) continue;

                string fingerprint = McpServerFingerprint.Of(server);
                if (runtime.Fingerprint.Length == 0)
                {
                    runtime.Fingerprint = fingerprint; //首次登记,不算变化
                    continue;
                }

                if (string.Equals(runtime.Fingerprint, fingerprint, StringComparison.Ordinal)) continue;

                if (runtime.Client != null) orphans.Add(runtime.Client);
                // 整条换新而不是逐字段清:退避账也该重算(配置换了,上次为什么连不上不再有参考价值)。
                // 只把"上次的账"带过去给 UI
                _runtimes[key] = new McpServerRuntime
                {
                    Fingerprint = fingerprint,
                    LastToolCount = runtime.LastToolCount,
                    EstimatedTokens = runtime.EstimatedTokens,
                    LastUsedUtc = DateTime.UtcNow,
                };
                Log.Debug($"MCP '{key}' config changed; connection dropped and awaiting reconnect");
            }
        }

        foreach (McpClient client in orphans) _ = client.DisposeAsync();
    }

    /// 一份连接租约。重复释放安全——using 与显式 Dispose 撞上时不该把计数减穿
    private sealed class McpLease : IDisposable
    {
        private readonly McpManager _owner;
        private readonly string _workspace;
        private bool _released;

        public McpLease(McpManager owner, string workspace)
        {
            _owner = owner;
            _workspace = workspace;
        }

        public void Dispose()
        {
            if (_released) return;
            _released = true;
            _owner.ReleaseLease(_workspace);
        }
    }

    //================= 预告（装配之前就能看见的那份名单） =================

    /// <summary>
    /// 这个会话<b>将会</b>接入哪些 MCP server。
    ///
    /// 为什么需要它：能力面板的数据源是装配产物，而智能体会话的执行者是<b>懒建的</b>——
    /// 首轮发送之前那一栏是空的，于是新会话是「不告而连」。这一处回答的是装配之前的那个问题。
    ///
    /// <b>它是预测，不是实况</b>，所以刻意与装配产物分开返回、界面上也要分开呈现：
    /// 能力面板那句「数据源是装配产物本身，不是能力开关的二次推导」必须继续成立，
    /// 否则以后排查「面板说挂了但模型没有」会多一层。
    ///
    /// 被同名覆盖掉的全局 server <b>也在列表里</b>（<c>IsShadowed</c>），因为覆盖是这套设计里
    /// 唯一一条会让「我明明配了却没生效」发生的规则，藏起来省的那一行不值。
    ///
    /// <b>未托管的整条不出现</b>（连它被覆盖的孪生条目一起）。这一栏其余三个「没挂上」的理由
    /// ——被覆盖、待授权、被角色禁用——都是会话级且能就地处理的；而托管开关按 ADR 0008
    /// 是<b>资源层</b>的全局设置，跟这次会话、这个角色、这个工作区都无关。把它摆进能力面板，
    /// 等于让用户在每个会话里重看一遍自己早已关掉的东西，也正是 0008 要消除的「要查两处」。
    /// </summary>
    /// <param name="workspacePath">会话绑定的工作区；空表示未绑定</param>
    /// <param name="disabledServers">本角色禁用的 server 名单</param>
    /// <returns>预告名单，项目级在前</returns>
    public List<McpPlannedServer> GetPlannedServers(string? workspacePath,
        IEnumerable<string>? disabledServers = null)
    {
        HashSet<string> disabled = DisabledSet(disabledServers);
        List<McpPlannedServer> planned = new();
        foreach (EffectiveMcpServer effective in GetEffectiveServers(workspacePath))
        {
            // 胜出者不托管则整组跳过:只留下一条"我被覆盖了"却看不见覆盖谁,比不显示更费解
            if (!effective.Config.IsEnabled) continue;

            planned.Add(Describe(effective.Config, disabled, isShadowed: false));
            // 被顶掉的那条紧跟在胜出者后面:两条并排才看得出"覆盖"这件事
            if (effective.Shadowed != null)
            {
                planned.Add(Describe(effective.Shadowed, disabled, isShadowed: true));
            }
        }

        return planned;
    }

    private McpPlannedServer Describe(McpServerConfig server, HashSet<string> disabled, bool isShadowed)
    {
        McpServerStatus status = GetServerStatus(server.Name, server.WorkspacePath);
        bool trusted = _trust.IsTrusted(server);
        return new McpPlannedServer
        {
            Name = server.Name,
            WorkspacePath = server.WorkspacePath,
            CommandLine = DescribeCommand(server),
            IsShadowed = isShadowed,
            IsHostingOff = !server.IsEnabled,
            IsDisabledByCharacter = disabled.Contains(server.Name),
            NeedsApproval = !trusted,
            // 未授权时状态一律报待确认:此刻运行态上可能还留着上一次的 Failed
            State = trusted ? status.State : EMcpConnectionState.PendingApproval,
            LastToolCount = status.LastToolCount,
            EstimatedTokens = status.EstimatedTokens,
        };
    }

    //================= 安全授权（项目级 .mcp.json） =================

    /// <summary>
    /// 该工作区里<b>尚待用户确认</b>的项目级 server。App 层在用户选定工作区那一刻问这一处，
    /// 把要执行的命令摆出来要一次确认。
    ///
    /// <b>刻意做成被动查询而不是事件</b>：弹窗是 UI 的事、授权是 Core 的事，这条缝切在这里。
    /// Core 侧的不变式只有一句「没记录就不连」，它不关心记录怎么来的；
    /// 而抛全局事件的做法这个仓库已经踩过——从前预连的提示就因此点亮到一个跟 MCP 毫无关系的会话上。
    /// </summary>
    /// <param name="workspacePath">工作区路径</param>
    /// <returns>待确认清单；无则空列表</returns>
    public List<McpApprovalRequest> GetPendingApprovals(string? workspacePath)
    {
        if (string.IsNullOrEmpty(workspacePath)) return new List<McpApprovalRequest>();

        List<McpApprovalRequest> pending = new();
        foreach (EffectiveMcpServer effective in GetEffectiveServers(workspacePath))
        {
            McpServerConfig server = effective.Config;
            if (!server.IsWorkspaceScoped || _trust.IsTrusted(server)) continue;

            pending.Add(new McpApprovalRequest
            {
                Name = server.Name,
                WorkspacePath = server.WorkspacePath!,
                CommandLine = DescribeCommand(server),
                IsChanged = _trust.WasApprovedWithDifferentCommand(server),
            });
        }

        return pending;
    }

    /// <summary>
    /// 记下用户给出的授权（按名字，指纹现取现算），随即让这些 server 参与下一次装配。
    /// </summary>
    /// <param name="workspacePath">工作区路径</param>
    /// <param name="names">用户确认放行的 server 名；传空表示该工作区的全部待确认项</param>
    public void ApproveWorkspaceServers(string workspacePath, IEnumerable<string>? names = null)
    {
        HashSet<string>? filter = names == null ? null : new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        List<McpServerConfig> approved = GetEffectiveServers(workspacePath)
            .Select(x => x.Config)
            .Where(x => x.IsWorkspaceScoped && (filter == null || filter.Contains(x.Name)))
            .ToList();
        if (approved.Count == 0) return;

        _trust.Approve(approved);
        lock (_lock)
        {
            // 状态从 PendingApproval 退回未连接,并让装配快照感知——否则要等下一次别的变化才会重建
            foreach (McpServerConfig server in approved)
            {
                McpServerRuntime runtime = GetRuntimeLocked(McpServerKey.Of(server));
                if (runtime.State == EMcpConnectionState.PendingApproval)
                {
                    runtime.State = EMcpConnectionState.Disconnected;
                }
            }

            _revision++;
        }
    }

    /// <summary>
    /// 撤销某工作区的全部项目级授权，并断开它们的连接。
    /// </summary>
    /// <param name="workspacePath">工作区路径</param>
    public void RevokeWorkspaceTrust(string workspacePath)
    {
        _trust.Revoke(workspacePath);

        string workspace = McpServerKey.NormalizeWorkspace(workspacePath);
        List<McpClient> orphans = new();
        lock (_lock)
        {
            foreach (McpServerKey key in _runtimes.Keys
                         .Where(x => string.Equals(x.Workspace, workspace, StringComparison.Ordinal)).ToList())
            {
                if (!_runtimes.Remove(key, out McpServerRuntime? runtime)) continue;
                if (runtime.Client != null) orphans.Add(runtime.Client);
            }

            _revision++;
        }

        foreach (McpClient client in orphans) _ = client.DisposeAsync();
    }

    /// <summary>已授权过的工作区清单（设置页的"改主意"入口据此列出）</summary>
    /// <returns>规范化后的工作区绝对路径</returns>
    public List<string> GetTrustedWorkspaces() => _trust.GetTrustedWorkspaces();

    /// 确认框上要摆的那一行:用户批的是这条命令,不是这个名字
    private static string DescribeCommand(McpServerConfig server)
    {
        return server.TransportType == EMcpTransportType.Http
            ? server.Url
            : string.Join(' ', new[] { server.Command }.Concat(server.Args)).Trim();
    }

    /// <summary>
    /// 这一轮要不要管这个 server：<b>托管</b>开着（连接层，全局一份），
    /// 且没被本角色<b>禁用</b>（能力层，按角色，见 ADR 0008）。
    ///
    /// 装配（<see cref="Resolve"/>）与按名查找共用这一处，两边不会各算一遍。
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
    private McpServerRuntime GetRuntimeLocked(McpServerKey key)
    {
        if (_runtimes.TryGetValue(key, out McpServerRuntime? runtime)) return runtime;
        runtime = new McpServerRuntime();
        _runtimes[key] = runtime;
        return runtime;
    }

    /// <summary>调用方须持有 _lock。受退避约束，force 时无视退避</summary>
    private void KickRefreshLocked(McpServerConfig server, bool force)
    {
        McpServerRuntime runtime = GetRuntimeLocked(McpServerKey.Of(server));
        if (runtime.Refreshing) return;
        if (!force && DateTime.UtcNow < runtime.NextAttemptUtc) return;

        runtime.Refreshing = true;
        // 任务留在运行态上:模型查、调时要等的就是它。丢掉句柄的话「等它连上」无从实现
        runtime.RefreshTask = Task.Run(() => RefreshServerAsync(server, CancellationToken.None));
    }

    private async Task RefreshServerAsync(McpServerConfig server, CancellationToken cancellationToken)
    {
        McpServerKey key = McpServerKey.Of(server);
        string fingerprint = McpServerFingerprint.Of(server);
        lock (_lock)
        {
            //手动路径进来时标记还没置上;后台路径由 KickRefreshLocked 置好,这里是幂等补一次
            McpServerRuntime runtime = GetRuntimeLocked(key);
            runtime.Refreshing = true;
            if (runtime.Fingerprint.Length == 0) runtime.Fingerprint = fingerprint;
        }

        try
        {
            McpClient client = await ConnectAsync(server, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<AIFunction> tools = await client
                .ListAgentToolsWithTasksAsync(options: null, cancellationToken: cancellationToken).ConfigureAwait(false);
            HashSet<string> readOnly = await ReadOnlyToolsAsync(client, key, cancellationToken).ConfigureAwait(false);

            bool stale = false;
            McpToolCatalog? catalog = null;
            lock (_lock)
            {
                McpServerRuntime runtime = GetRuntimeLocked(key);
                // 配置在这次连接期间被改过:这批工具属于旧命令,写进去等于让新配置顶着旧工具跑。
                // 连接本身已由 SyncFingerprints 作废,这里只需别把结果落进新的运行态
                if (!string.Equals(runtime.Fingerprint, fingerprint, StringComparison.Ordinal))
                {
                    stale = true;
                }
                else
                {
                    runtime.Tools = tools;
                    runtime.ReadOnlyTools = readOnly;
                    runtime.LastToolCount = tools.Count;
                    runtime.LastUsedUtc = DateTime.UtcNow;
                    // 取回时就地算好:分词有代价,而状态是设置页与角色编辑页在 UI 线程上问的
                    runtime.EstimatedTokens = ToolTokenEstimator.Estimate(tools);
                    // server 自述:MCP initialize 响应里 server 用来说明"怎么用我这套工具"的官方口子。
                    // 丢掉它等于让模型只看见工具签名、看不见用法
                    runtime.Instructions = client.ServerInstructions?.Trim() ?? string.Empty;
                    runtime.State = EMcpConnectionState.Connected;
                    runtime.Error = null;
                    runtime.FailureCount = 0;
                    runtime.NextAttemptUtc = DateTime.MinValue;

                    // server 离线时帮助读的就是这份
                    catalog = new McpToolCatalog
                    {
                        Fingerprint = fingerprint,
                        Instructions = runtime.Instructions,
                        Tools = tools.Select(McpToolDescriptor.From).ToList(),
                        CapturedUtc = DateTime.UtcNow,
                    };
                }
            }

            if (catalog != null) _catalogs.Save(key, catalog);

            if (stale)
            {
                Log.Debug($"MCP '{key}' config changed while connecting; discarding that connection");
                await client.DisposeAsync().ConfigureAwait(false);
                return;
            }

            EnsureReclaimLoop(); //有连接了才值得开回收循环
        }
        catch (Exception e)
        {
            Log.Warning($"MCP server '{key}' unavailable: {e.Message}");
            lock (_lock)
            {
                McpServerRuntime runtime = GetRuntimeLocked(key);
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
                GetRuntimeLocked(key).Refreshing = false;
            }
        }
    }

    // 框架包过的工具（任务感知那层）不透出协议注解,只读声明得另问一次清单。
    // 问不到不算连接失败:少了它只是每次调用都问用户
    private static async Task<HashSet<string>> ReadOnlyToolsAsync(McpClient client, McpServerKey key,
        CancellationToken cancellationToken)
    {
        try
        {
            IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return tools.Where(x => x.ProtocolTool.Annotations?.ReadOnlyHint == true)
                .Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.Debug($"MCP '{key}' tool annotations unavailable: {e.Message}");
            return [];
        }
    }

    private async Task<McpClient> ConnectAsync(McpServerConfig server, CancellationToken cancellationToken)
    {
        McpServerKey key = McpServerKey.Of(server);
        McpClient? existing;
        lock (_lock)
        {
            McpServerRuntime runtime = GetRuntimeLocked(key);
            existing = runtime.Client;
            if (existing == null) runtime.State = EMcpConnectionState.Connecting;
        }

        if (existing != null) return existing;

        McpClient client = await McpTransportFactory.CreateClientAsync(server, cancellationToken)
            .ConfigureAwait(false);

        McpClient? raced = null;
        lock (_lock)
        {
            McpServerRuntime runtime = GetRuntimeLocked(key);
            // 后台补连与手动测试可能同时进到这里,谁先落谁算数,输的那个连接必须收掉
            if (runtime.Client != null) raced = client;
            else runtime.Client = client;
            client = runtime.Client;
        }

        if (raced != null) _ = raced.DisposeAsync();
        return client;
    }

    /// <summary>
    /// 丢弃一个 server 的运行态并断开连接，顺带清掉<b>已不在配置里的那些</b>。
    ///
    /// 后半句是必须的：改名保存时，新名字这条会被重建，而旧名字那条既不会再被任何人问到，
    /// 也不会自己退出——stdio 的话那是一个留在后台的子进程。
    /// </summary>
    /// 顺带清理<b>只针对全局作用域</b>：<c>_servers</c> 就是那一份名单，拿它去判项目级的存活
    /// 会把别的工作区正在用的连接一并误杀。项目级的失效由配置指纹与空闲回收负责。
    /// <param name="target">要重建的那一条</param>
    private void DropRuntime(McpServerKey target)
    {
        List<McpClient> orphans = new();
        lock (_lock)
        {
            HashSet<string> alive = _servers.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (McpServerKey key in _runtimes.Keys.ToList())
            {
                if (!key.Equals(target) && (!key.IsGlobal || alive.Contains(key.Name))) continue;
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
        SaveUtility.Save(AppPaths.Config.McpServers, standard);
        SaveUtility.Save(AppPaths.Config.McpServerStates, states);
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

        /// 正在跑的那次刷新;模型查、调时据此等待。失败过的那次是已完成任务,等于不等
        public Task? RefreshTask { get; set; }

        public int FailureCount { get; set; }
        public DateTime NextAttemptUtc { get; set; }

        /// 自己声明了只读（readOnlyHint）的工具名;没连上过为 null(审批按「不是只读」算)
        public HashSet<string>? ReadOnlyTools { get; set; }

        /// 建立这份运行态时那条配置的可执行面指纹;变了就得整条作废(见 SyncFingerprints)
        public string Fingerprint { get; set; } = string.Empty;

        /// 最后一次被使用的时刻(取/还租约时刷新);零租约后据此计空闲
        public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 上次连上时取回的工具数。<b>空闲回收后不清</b>——面板要显示「已断开（上次 12 个工具）」，
        /// 显示成 0 是在撒谎，用户会以为这个 server 坏了。
        /// </summary>
        public int? LastToolCount { get; set; }
    }

}
