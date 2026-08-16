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
    /// <summary>标准配置文件的完整路径。设置页展示它,用户可直接编辑或整段替换</summary>
    public static string ConfigFilePath => AppPaths.Config.McpServers;

    /// <summary>
    /// 装配前等 server 连上的上限。取得比一次普通请求还长是有意的：
    /// 等一会儿换来「这一轮真的带着工具跑」，比让第一轮静默缺工具划算得多。
    /// 而这笔等待每个进程只付一次——工具取回后就常驻缓存。
    /// </summary>
    private static readonly TimeSpan WarmupTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 零租约之后连接留存多久。
    ///
    /// <b>为什么不是「会话切走就断」</b>：工作区是<b>会话级</b>字段（<c>ChatSession.WorkspacePath</c>），
    /// 不是应用级状态——不同会话可以同时挂在不同工作区上，而定时任务更会在无头会话里跑。
    /// 照「切走即断」实现会掐掉正在后台跑的那一轮。
    ///
    /// <b>也不是永不回收</b>：stdio server 意味着常驻子进程，试过五个项目就是五窝进程挂到应用退出。
    ///
    /// 取一小时是因为在项目之间来回切是常态，而重连要付一次 <see cref="WarmupTimeout"/>。
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
    private readonly Dictionary<string, int> _leases = new(StringComparer.Ordinal); //在途租约,键同索引键的作用域那一半
    private CancellationTokenSource? _reclaimCancellation; //空闲回收循环,首次连上时惰性启动
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
                HasInstructions = runtime.Instructions.Length > 0,
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
    /// 解析出一次装配要用的 MCP 工具集与自述。<b>同步取缓存，绝不等待网络</b>；
    /// 尚未取回工具的 server 在后台补连（受退避约束），完成后修订号自增，
    /// 下一次挂接经装配快照差异自动重建。
    /// </summary>
    /// <param name="workspacePath">
    /// 本次装配的工作区；项目级 <c>.mcp.json</c> 里的 server 据此并入（同名覆盖全局）。
    /// 空表示未绑定工作区，只有全局 server 参与。
    /// </param>
    /// <param name="disabledServers">本角色禁用的 server 名单（能力层，见 ADR 0008）</param>
    /// <returns>工具集、分组明细与自述</returns>
    public McpToolSet Resolve(string? workspacePath = null, IEnumerable<string>? disabledServers = null)
    {
        List<McpServerConfig> inPlay = InPlayServers(workspacePath, DisabledSet(disabledServers));

        List<ResolvedMcpServer> resolved = new();
        lock (_lock)
        {
            foreach (McpServerConfig server in inPlay)
            {
                McpServerRuntime runtime = GetRuntimeLocked(McpServerKey.Of(server));
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
    /// <param name="workspacePath">本次装配的工作区；与 <see cref="Resolve"/> 取同一个值</param>
    /// <param name="disabledServers">本角色禁用的 server 名单（能力层，见 ADR 0008）</param>
    /// <param name="waiting">
    /// 真的要等时回调 <c>true</c>、等完回调 <c>false</c>；无需等待时<b>一次都不回调</b>。
    /// 界面据此提示——这段等待发生在用户按下发送之后，可能长达十秒，不说一声就是十秒的"看着像卡死"。
    ///
    /// 刻意做成<b>逐次调用的回调</b>而不是这个单例上的一个状态：等待是<b>调用方那一轮</b>的事，
    /// 而连接是全进程共享的。从前这里挂着一个全局 bool 加一个全局事件，
    /// 于是后台定时任务触发的预连会点亮一个跟 MCP 毫无关系的会话的提示，
    /// 两条链路并发时先结束的那个还会把另一个的提示掐灭。
    /// </param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task WarmupAsync(string? workspacePath = null,
        IEnumerable<string>? disabledServers = null,
        Action<bool>? waiting = null,
        CancellationToken cancellationToken = default)
    {
        List<McpServerConfig> inPlay = InPlayServers(workspacePath, DisabledSet(disabledServers));

        List<Task> pending = new();
        lock (_lock)
        {
            foreach (McpServerConfig server in inPlay)
            {
                McpServerRuntime runtime = GetRuntimeLocked(McpServerKey.Of(server));
                if (runtime.Tools != null) continue; //已经取回,不必再等

                KickRefreshLocked(server, force: false);
                // 退避中的不会有新任务,而留在运行态上的是上一次那个已完成的任务——等它等于不等。
                // 但那样 pending 非空会让预连提示白闪一帧,所以只把真正没跑完的算进来
                if (runtime.RefreshTask is { IsCompleted: false } task) pending.Add(task);
            }
        }

        if (pending.Count == 0) return;

        waiting?.Invoke(true);
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
            waiting?.Invoke(false);
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
    /// 合并只在这一处做——装配、预连、能力面板三条路都从这里取，不各自算一遍。
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
                _revision++;
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
                _revision++;
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
    /// 而抛全局事件的做法这个仓库已经踩过——预连提示曾经因此点亮到一个跟 MCP 毫无关系的会话上
    /// （见 <see cref="WarmupAsync"/> 的 waiting 参数注释）。
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
        // 任务留在运行态上:WarmupAsync 要等的就是它。丢掉句柄的话「等它连上」无从实现
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
                .ListAgentToolsWithTaskSupportAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            bool stale = false;
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
                    _revision++;
                }
            }

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
        McpServerKey key = McpServerKey.Of(server);
        McpClient? existing;
        lock (_lock)
        {
            McpServerRuntime runtime = GetRuntimeLocked(key);
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
            McpServerRuntime runtime = GetRuntimeLocked(key);
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

        /// 正在跑的那次刷新;WarmupAsync 据此等待。失败过的那次是已完成任务,等于不等
        public Task? RefreshTask { get; set; }

        public int FailureCount { get; set; }
        public DateTime NextAttemptUtc { get; set; }

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

/// <summary>
/// 预告名单里的一条：这个会话<b>将会</b>（或本该、却没能）接入的一个 server。
///
/// 与 <see cref="McpServerToolGroup"/> 的分工：那个是<b>实况</b>（装配之后真挂上了什么），
/// 这个是<b>预告</b>（装配之前的预测）。两者不合并，界面上也分区呈现。
/// </summary>
public sealed class McpPlannedServer
{
    /// <summary>server 名</summary>
    public required string Name { get; init; }

    /// <summary>来源工作区；<c>null</c> 即全局配置</summary>
    public string? WorkspacePath { get; init; }

    /// <summary>是否来自项目级 <c>.mcp.json</c></summary>
    public bool IsWorkspaceScoped => WorkspacePath != null;

    /// <summary>将要执行的命令（stdio）或将要连接的地址（http）</summary>
    public string CommandLine { get; init; } = string.Empty;

    /// <summary>
    /// 本条是被项目级同名配置<b>顶掉</b>的那个全局 server。
    /// 界面要灰显并写明原因——否则用户不知道自己全局那个被吃掉了。
    /// </summary>
    public bool IsShadowed { get; init; }

    /// <summary>连接层关着（<c>IsEnabled</c> 为 false，见 ADR 0008）</summary>
    public bool IsHostingOff { get; init; }

    /// <summary>被本角色的黑名单禁用（能力层，见 ADR 0008）</summary>
    public bool IsDisabledByCharacter { get; init; }

    /// <summary>项目级配置尚未通过安全确认</summary>
    public bool NeedsApproval { get; init; }

    /// <summary>此刻的连接状态</summary>
    public required EMcpConnectionState State { get; init; }

    /// <summary>上次连上时的工具数；从未连过为 null</summary>
    public int? LastToolCount { get; init; }

    /// <summary>上次算出的工具定义估算 token；从未连过为 null</summary>
    public int? EstimatedTokens { get; init; }

    /// <summary>这一轮真的会挂上去（三道闸门都过了，且已连上）</summary>
    public bool WillBeMounted => !IsShadowed && !IsHostingOff && !IsDisabledByCharacter && !NeedsApproval;
}

/// <summary>
/// 一条待用户确认的项目级 server。确认框上要摆的就是这三样：
/// 谁、来自哪个项目、<b>将要执行什么</b>。
/// </summary>
public sealed class McpApprovalRequest
{
    /// <summary>server 名</summary>
    public required string Name { get; init; }

    /// <summary>来源工作区</summary>
    public required string WorkspacePath { get; init; }

    /// <summary>将要执行的命令（stdio）或将要连接的地址（http）</summary>
    public required string CommandLine { get; init; }

    /// <summary>
    /// 之前授权过、这次是<b>命令被改了</b>。界面要把它与"新增的一条"分开说——
    /// 前者更值得警惕：那意味着仓库里有人动过将在你机器上执行的东西。
    /// </summary>
    public bool IsChanged { get; init; }
}

/// <summary>
/// 一个 server 的状态快照（UI 展示用）
/// </summary>
public sealed class McpServerStatus
{
    /// <summary>连接状态</summary>
    public EMcpConnectionState State { get; init; }

    /// <summary>此刻已取回、真正可调用的工具数</summary>
    public int ToolCount { get; init; }

    /// <summary>
    /// 上次连上时的工具数；空闲回收或配置变更后仍保留。
    /// 界面据此显示「已断开（上次 12 个工具）」——回收之后报 0 是在撒谎，
    /// 用户会以为这个 server 坏了。从未连过时为 null（那是「不知道」）。
    /// </summary>
    public int? LastToolCount { get; init; }

    /// <summary>工具定义的估算 token 数；尚未取回工具时为 null（那是「不知道」，不是 0）</summary>
    public int? EstimatedTokens { get; init; }

    /// <summary>失败原因；成功或未连接时为 null</summary>
    public string? Error { get; init; }

    /// <summary>server 是否给了自述</summary>
    public bool HasInstructions { get; init; }
}
