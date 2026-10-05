/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Concurrent;
using UiharuMind.Core.AI.Chat;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Delivery;
using UiharuMind.Core.AI.Execution.Prompts;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>子代理后台跑着时，需要让用户知道的那几件事（不带文案，本地化属界面层）</summary>
public enum ESubAgentNotice
{
    /// <summary>有一张嵌套审批卡在等人点选。<b>承重</b>：到期按拒绝收口，这条弹不出来那次委派就白跑</summary>
    ApprovalWaiting,

    /// <summary>这次委派已经跑了很久（见 <see cref="BackgroundSubAgentDispatcher.LongRunNotice"/>）</summary>
    LongRunning,
}

/// <summary>
/// 后台子代理的调度处：**派出之后到报告交回之前**那一段归它。
///
/// 它刻意<b>不认识子代理</b>——跑什么由调用方给一个委托，这里只管两件事：
/// 后台生命周期（含长跑提示）与回信送达（经 <see cref="SessionDelivery"/>，醒着插进那一轮、闲着落盘再叫醒）。
///
/// 为什么要有唤醒轮见 ADR 0025，回信怎么送见 ADR 0062。
/// </summary>
public static class BackgroundSubAgentDispatcher
{
    /// <summary>跑到这个时长还没完，弹一条提示把它顶到用户眼前。
    /// <c>SubAgentTool.Timeout</c>（24 小时）刻意没动，靠这条提示兜住「悄悄烧了一夜」</summary>
    public static readonly TimeSpan LongRunNotice = TimeSpan.FromMinutes(30);

    private const string EmptyReport = "没写完回复就停下了"; //什么都没产出就停了:接在「对方」后面成句

    // 父会话 → 名下未交回的子会话标识。记 id 而不是计数,是因为界面要按父会话问
    // 「名下有没有一个卡在审批上」——那件事只有子会话标识答得出
    private static readonly ConcurrentDictionary<string, HashSet<string>> _pendingByParent = new();

    // 反向索引:子会话标识 → 还有几轮未交回。一张工具卡只知道自己那个子会话标识,
    // 要问的是「这一次委派交回了没有」;同一子会话可以排队多轮(主代理插话回落续跑),
    // 所以按轮次计数而不是按 id 是否存在,否则先交回的那轮会把还在跑的后续轮次一起摘掉。
    private static readonly ConcurrentDictionary<string, int> _pendingRoundsById = new();
    // 同 id 轮次的 Add/Remove 互斥:消除「减到 0 后、TryRemove 前，新一轮 Add 被连带摘掉」的竞态
    private static readonly ConcurrentDictionary<string, object> _roundLocks = new();
    // 轮次记账:父会话 → 名下未交回的后台轮次总数。数量统计按这个算——按 id 记(set)
    // 表达不了「同一子会话排队多轮」,计数会在先交回的那轮被清成 0,还在跑的后轮隐身。
    // 与 _pendingByParent 互补:后者按 id 供三档显示/按 id 查询,这个按轮次供计数。
    private static readonly ConcurrentDictionary<string, int> _pendingTurnCountByParent = new();
    // 同子会话的交回串行闸:轮次只串行 run,交回另起闸防止逆序——先跑完的那份必须先交回
    // (ParentBusy 重试节律彼此独立,不串的话旧报告可能原地替换掉新报告)。只在交回期间持有,
    // 不占 _turnGates,不影响排队轮与窗口直发。
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _deliverGates = new();

    // 父会话 → 报告已就绪、正等它空闲好交回的子会话标识。它是 _pendingByParent 的<b>子集</b>,
    // 单独记是因为界面要把「还在跑」与「跑完了压着」分成两行说——用户看到的
    // 「子代理跑完了、回执没了」正是后一档(派活者正在跑时写它的历史会与落盘交错,只能等)
    private static readonly ConcurrentDictionary<string, HashSet<string>> _handoffQueuedByParent = new();

    // 子会话 → 本会话同一时刻只跑一轮的串行闸。一次委派 = 真会话 + 自己的 TurnDriver,
    // 但执行者是会话本体的惰性单例——两轮一旦重叠,后一轮 Attach 完、前一轮 finally
    // 就把 runner 释放掉,读到的就是个没挂接的新 runner,当场炸「尚未挂接会话」
    // (实机:同一子会话同秒结束两轮,一轮正常、一轮空报告)。
    // 旧 Continue 在跑着时也起新轮,堆叠是常态;而执行者内部那把锁是单 runner 实例的,
    // 释放重建就换了一把,拦不住跨轮重叠——只能在这里按子会话串行,跑 + 交回原子地走完,
    // 后到的轮次在闸门外等(不烧资源),交回落盘之后才开跑,报告时序也不倒置。
    // 常驻不删:删了就得在「删与等」之间再加一把锁,而子会话本来就只增不减,多一份小锁是同类欠账。
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _turnGates = new();

    /// <summary>
    /// 进入某个子会话的轮次闸门（与后台轮共用同一把锁，见 <see cref="_turnGates"/>）。
    /// 主会话不过闸：它的后台轮另走 <c>TryBeginRun</c> 抢占。返回 <c>null</c> 表示不用串行。
    /// 前台直发（用户在子会话窗口打字）与后台派出的轮次（Continue/唤醒）共用同一个执行者，
    /// 两轮一旦重叠就是 runner 释放/重建竞态——必须同入一把闸。
    /// </summary>
    public static async Task<IDisposable?> EnterSubSessionTurnGateAsync(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return null;
        if (SessionManager.Instance.GetMeta(sessionId)?.IsSubSession != true) return null;

        SemaphoreSlim gate = _turnGates.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        return new TurnGateLease(gate);
    }

    private sealed class TurnGateLease : IDisposable
    {
        private readonly SemaphoreSlim _gate;
        private int _released;

        public TurnGateLease(SemaphoreSlim gate) => _gate = gate;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) _gate.Release();
        }
    }

    /// <summary>某个会话「未了结的工作」变化。注意它<b>不是</b> IsGenerating，见 CONTEXT.md</summary>
    public static event Action<string>? PendingWorkChanged;

    /// <summary>
    /// 需要让用户知道的事往哪儿送。由界面注入（<c>IMessageService</c> 在 App 项目，Core 引不到，
    /// 同 ADR 0021「为什么 Core 建 driver，不是 UI」）；无界面场合不设，调用处是空操作。
    /// </summary>
    public static Action<ESubAgentNotice, string>? Notifier { get; set; }

    /// <summary>测试/诊断用：该 id 是否还在父会话的「按 id 追踪」集合里（三档显示口径）</summary>
    internal static bool IsSubSessionTracked(string parentId, string subSessionId)
    {
        if (!_pendingByParent.TryGetValue(parentId, out HashSet<string>? ids)) return false;
        lock (ids) return ids.Contains(subSessionId);
    }

    /// <summary>这个会话名下还有没有未交回的后台委派（<b>未了结的工作</b>的一半，另一半是它自己在不在跑）</summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>有则 true</returns>
    public static bool HasPendingWork(string? sessionId) => PendingCount(sessionId) > 0;

    /// <summary>这个会话名下未交回的后台委派<b>轮次数</b>（右栏面板显示用；同一子会话排队多轮时按轮计，不按 id 计）</summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>条数</returns>
    public static int PendingCount(string? sessionId) =>
        !string.IsNullOrEmpty(sessionId)
        && _pendingTurnCountByParent.TryGetValue(sessionId, out int n) ? n : 0;

    /// <summary>登记一轮未交回的后台委派（<c>Dispatch</c> 时每派一轮调一次；同一子会话可登记多次）</summary>
    internal static void AddPendingTurn(string parentId, string subSessionId)
    {
        if (string.IsNullOrEmpty(subSessionId)) return;
        lock (_roundLocks.GetOrAdd(subSessionId, _ => new object()))
        {
            HashSet<string> pending = _pendingByParent.GetOrAdd(parentId, _ => new HashSet<string>());
            lock (pending) pending.Add(subSessionId);

            _pendingRoundsById.AddOrUpdate(subSessionId, 1, static (_, n) => n + 1);
            _pendingTurnCountByParent.AddOrUpdate(parentId, 1, static (_, n) => n + 1);
        }
    }

    /// <summary>注销一轮未交回的后台委派（<c>DeliverAsync</c> 收尾时每交回一轮调一次）</summary>
    internal static void RemovePendingTurn(string parentId, string subSessionId)
    {
        if (string.IsNullOrEmpty(subSessionId)) return;
        lock (_roundLocks.GetOrAdd(subSessionId, _ => new object()))
        {
            // 子会话还有别的轮次在跑/在排就保留 id 与反向索引;归零才摘——否则先交回的那轮
            // 会把还在跑的排队轮从「三档显示」里一起摘掉(与轮次计数同源的 id 级隐身)
            if (_pendingRoundsById.AddOrUpdate(subSessionId, 0, static (_, n) => Math.Max(0, n - 1)) == 0)
            {
                _pendingRoundsById.TryRemove(subSessionId, out _);
                if (_pendingByParent.TryGetValue(parentId, out HashSet<string>? pending))
                {
                    lock (pending) pending.Remove(subSessionId);
                }
            }

            // 父级计数只减不摘:键值可为 0(PendingCount 读 0 无害)。删除动作与新一轮 Add 之间
            // 有竞态窗口——不同子会话持不同 roundLock,新派发的 Add 可能被刚归零的 TryRemove
            // 连带摘掉(与 _roundLocks 盖不到的同类竞态)。键只增不减,与既有欠账同口径。
            _pendingTurnCountByParent.AddOrUpdate(parentId, 0, static (_, n) => Math.Max(0, n - 1));
        }
    }

    /// <summary>
    /// 名下有没有后台子代理<b>卡在审批上等人点选</b>。
    ///
    /// 与「在跑」分开问：它是唯一需要用户<b>动手</b>的状态，而且是有时限的——
    /// <c>SubAgentTool.NestedApprovalTimeout</c> 到期按拒绝收口，那次委派基本白跑。
    /// 所以它在界面上必须与「在跑」区分得开，不能共用一个绿点。
    /// </summary>
    /// <param name="sessionId">父会话标识</param>
    /// <returns>有则 true</returns>
    public static bool HasApprovalWaiting(string? sessionId) => ApprovalWaiting(sessionId).Count > 0;

    /// <summary>
    /// 这一次委派<b>派出去了但报告还没交回</b>。
    ///
    /// 父会话流里那张工具卡靠它显示「已派出 / 结果待回」——工具当场就返回了，
    /// 光看调用有没有结果的话，卡片在派出后一秒内就变成「成功」，而子代理还在跑
    /// （见 ADR 0021 预付的那一档、ADR 0025）。
    /// </summary>
    /// <param name="subSessionId">子会话标识</param>
    /// <returns>还没交回则 true</returns>
    public static bool IsAwaitingReport(string? subSessionId) =>
        !string.IsNullOrEmpty(subSessionId) && _pendingRoundsById.ContainsKey(subSessionId);

    /// <summary>
    /// 名下正卡在审批上的子会话，按标识列出。
    ///
    /// 界面靠它做<b>常驻入口</b>：通知会飘走、工具卡会滚走，而这一份随时问随时答。
    /// </summary>
    /// <param name="sessionId">父会话标识</param>
    /// <returns>子会话标识；没有则空</returns>
    public static IReadOnlyList<string> ApprovalWaiting(string? sessionId)
    {
        List<string> waiting = [];
        foreach (string subSessionId in Snapshot(sessionId))
        {
            if (SessionManager.Instance.Running.StateOf(subSessionId) == ESessionRunState.AwaitingApproval)
            {
                waiting.Add(subSessionId);
            }
        }

        return waiting;
    }

    /// <summary>
    /// 名下<b>正在跑</b>的后台子代理，按标识列出：派出去了、既没卡在审批上、也还没排队等交回。
    ///
    /// 与 <see cref="ApprovalWaiting"/>、<see cref="HandoffQueued"/> 三者互斥且同源，
    /// 界面据此把一件事的三种处境分行显示——从前只有「等审批」那一行看得见，
    /// 子代理闷头跑着的时候输入区上方什么都没有，看着就像没派出去。
    /// </summary>
    /// <param name="sessionId">父会话标识</param>
    /// <returns>子会话标识；没有则空</returns>
    public static IReadOnlyList<string> RunningSubSessions(string? sessionId)
    {
        HashSet<string> queued = QueuedSnapshot(sessionId);
        List<string> running = [];
        foreach (string subSessionId in Snapshot(sessionId))
        {
            if (queued.Contains(subSessionId)) continue;
            if (SessionManager.Instance.Running.StateOf(subSessionId) == ESessionRunState.AwaitingApproval) continue;
            running.Add(subSessionId);
        }

        return running;
    }

    /// <summary>
    /// 名下<b>结论已就绪、正等派活者空闲</b>的子会话，按标识列出。
    ///
    /// 这一档必须看得见：派活者跑一轮长的，报告就压一轮那么久，而界面上一个字都没有——
    /// 用户看到的是「子代理跑完了，回执没了」（实机反馈）。
    /// </summary>
    /// <param name="sessionId">父会话标识</param>
    /// <returns>子会话标识；没有则空</returns>
    public static IReadOnlyList<string> HandoffQueued(string? sessionId) => QueuedSnapshot(sessionId).ToArray();

    /// <summary>进程内有没有<b>任何</b>后台委派还没交回（菜单栏图标那一档是全局的，不按会话分）</summary>
    public static bool AnyPending() => !_pendingRoundsById.IsEmpty;

    /// <summary>进程内有没有<b>任何</b>后台委派卡在审批上等人点选</summary>
    public static bool AnyApprovalWaiting()
    {
        foreach (string subSessionId in _pendingRoundsById.Keys)
        {
            if (SessionManager.Instance.Running.StateOf(subSessionId) == ESessionRunState.AwaitingApproval)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>取这个父会话名下未交回的子会话标识快照（读的时候集合可能正在被改，拷一份）</summary>
    private static string[] Snapshot(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return [];
        if (!_pendingByParent.TryGetValue(sessionId, out HashSet<string>? ids)) return [];
        lock (ids) return ids.ToArray();
    }

    /// <summary>取这个父会话名下正排队等交回的子会话标识快照</summary>
    private static HashSet<string> QueuedSnapshot(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return [];
        if (!_handoffQueuedByParent.TryGetValue(sessionId, out HashSet<string>? ids)) return [];
        lock (ids) return new HashSet<string>(ids);
    }

    /// <summary>记下「这一份在排队」。已经排着返回 false——调用方据此不要再起第二个重试循环</summary>
    private static bool MarkHandoffQueued(string parentId, string subSessionId)
    {
        HashSet<string> queued = _handoffQueuedByParent.GetOrAdd(parentId, _ => new HashSet<string>());
        lock (queued)
        {
            if (!queued.Add(subSessionId)) return false;
        }

        PendingWorkChanged?.Invoke(parentId);
        return true;
    }

    /// <summary>这一份不再排队（交回了、或者交不成了）</summary>
    private static void ClearHandoffQueued(string parentId, string subSessionId)
    {
        if (!_handoffQueuedByParent.TryGetValue(parentId, out HashSet<string>? queued)) return;
        bool removed;
        lock (queued) removed = queued.Remove(subSessionId);
        if (removed) PendingWorkChanged?.Invoke(parentId);
    }

    /// <summary>
    /// 把一次委派转入后台，<b>立即返回</b>。
    ///
    /// 调用方拿到的那句话会当场成为这次工具调用的结果（框架没有「挂起的工具结果」这种东西，
    /// 见 ADR 0025）。
    ///
    /// <b>它与 <c>SubAgentToolPrompts.AgentDescription</c> 分工，不要两边都写</b>：
    /// 工具描述是每轮重发的固定开销，说的是<b>政策</b>（发不发、后台意味着什么、
    /// 依赖它的事要等）——模型在<b>决定调用之前</b>读它。这里说的是<b>这一次的事实</b>
    /// （哪个子会话、此刻什么状态），它紧挨着模型的下一个 token，所以只留一条最强的护栏：
    /// 「尚无结果」。两边都写整段的话，固定开销和每次委派各付一遍钱，而多出来的那几句
    /// 并不会让模型更信。
    /// </summary>
    /// <param name="subSession">这次委派的子会话（已 <c>SessionManager.Add</c>）</param>
    /// <param name="run">跑这次委派并返回报告；传入的令牌与派活者那一轮<b>无关</b>（见下）</param>
    /// <param name="notice">
    /// 这一次要额外交代的事实，目前只有「点名的模型没生效、已回退」一种。
    /// <b>只在真的发生时才传</b>：回执是每次委派都付的钱，没发生的事不该占位。
    /// 它排在 <c>[sub-session: …]</c> 之前——那一行是跨模块契约，必须留在最后一行
    /// </param>
    /// <returns>当场返回给模型的工具结果</returns>
    public static string Dispatch(ChatSession subSession, Func<CancellationToken, Task<string>> run,
        string notice = "")
    {
        string parentId = subSession.ParentSessionId ?? string.Empty;
        // 这一轮的起点：首派与续跑都走这里，右栏「子代理」面板据此显示「本次已运行 / 末轮耗时」。
        // 排队续跑时它会把排队等待也算进本轮——可接受：排队通常很短，而字段必须随派发落盘才不丢
        subSession.LastRunStartedAt = DateTimeOffset.Now;
        subSession.BackgroundReportPending = true;
        subSession.SaveMeta();
        AddPendingTurn(parentId, subSession.SessionId);
        PendingWorkChanged?.Invoke(parentId);

        _ = Task.Run(() => RunInBackgroundAsync(subSession, parentId, run));

        // 结尾那行 `[sub-session: …]` 是**跨模块的契约**,不是随手写的格式:
        // 回放历史时卡片靠它认出「这是一次委派」并挂出「查看过程」入口
        // (ToolCallItem.ParseSubSessionId 的正则),而 SubSessionStartedContent 那条
        // 随当时那一轮就消失了。回信信头的署名用的是同一个格式
        // 写成「发给了某个人」:带上名字,说清回信只有一封、做完才写——问「好了没」换不来东西,
        // 不必再立「别催」的规矩;中途补充纠正是正当用法,照实说能做
        string who = subSession.SubAgentName.Length > 0 ? subSession.SubAgentName : subSession.SubAgentRole;
        return (who.Length > 0 ? $"Sent to \"{who}\". " : "Sent. ")
               + "They're working on it in their own conversation and will write back once, when done. "
               + $"You can `{SubAgentTool.MessageToolName}` them meanwhile to add or correct something — "
               + "they fold it into the reply they're already writing.\n"
               + (notice.Length > 0 ? notice + "\n" : string.Empty)
               // 给模型的是前 8 位短号(真实 ID 的别名),照抄进 to 由 SubSessionIdAlias 反查回真 ID
               + $"[sub-session: {SubSessionIdAlias.Short(subSession.SessionId)}]";
    }

    /// <summary>
    /// 启动时收口：上次进程退出时还在跑、或跑完了回信没送到的后台委派，父会话里那条「已派出」<b>永远等不到下文</b>。
    ///
    /// 父会话那一轮本身是自洽的（「已派出」那条工具结果早就配对落盘了），所以这里补的不是孤儿，
    /// 而是<b>语义上的断头</b>。不做自动续跑：用户隔了一次启动回来，未必还想要那件事。
    /// </summary>
    public static void SettleOrphansOnStartup()
    {
        foreach (ChatSessionMeta meta in SessionManager.Instance.GetSessions())
        {
            if (!meta.IsSubSession || !meta.BackgroundReportPending) continue;

            try
            {
                ChatSession? subSession = SessionManager.Instance.Load(meta.SessionId);
                if (subSession == null) continue;

                // 已经说完、只是回信没送到的，补送那一封；没说完的才是被中止
                string? report = subSession.PendingReport;
                subSession.BackgroundReportPending = false;
                subSession.PendingReport = null;
                subSession.SaveMeta();
                if (report == null) SubAgentReportHandoff.Submit(subSession, "在应用退出时被中止，没有跑完");
                else SubAgentReportHandoff.Submit(subSession, string.IsNullOrWhiteSpace(report) ? EmptyReport : null, report);
            }
            catch (Exception e)
            {
                //一个收不了不能拖累其余的
                Log.Warning($"Settle orphaned background sub-agent failed: session={meta.SessionId}: {e.Message}");
            }
        }
    }

    private static async Task RunInBackgroundAsync(ChatSession subSession, string parentId,
        Func<CancellationToken, Task<string>> run)
    {
        // 刻意<b>不</b>串派活者那一轮的令牌:那一轮在本方法开跑前就已经结束(工具当场返回了),
        // 串上去等于一派出就被取消。停止这一跑走 TurnDriver.CancelSession(子会话标识),
        // 右栏面板与子会话窗口的停止按钮用的正是它
        using CancellationTokenSource cancellation = new();
        using CancellationTokenSource longRun = new();
        _ = NoteLongRunAsync(subSession.SessionId, longRun.Token);

        // 同一子会话一次只跑一轮:堆叠的 Continue 在这里排队,而不是各跑各的执行者。
        // 派活者不被阻塞——Dispatch 当场就返回了,等的是 detached 的后台任务。
        // 串行范围<b>只包 run</b>:交回移到闸门外——它只写父会话历史、不碰子会话的
        // 执行者,而 ParentBusy 的重试可能无上限等待(父会话跑得久),压着闸会让同子会话
        // 的排队轮/窗口直发干等。交回自己的并发安全由父级写锁保证(见 SubAgentReportHandoff)。
        SemaphoreSlim gate = _turnGates.GetOrAdd(subSession.SessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        string report = string.Empty;
        try
        {
            try
            {
                report = await run(cancellation.Token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // 跑挂了也要走交回:子会话历史里已经留下了它做过什么,那就是此刻能给的全部结论。
                // 静默吞掉的话父会话里那条「已派出」永远没有下文
                Log.Warning($"Background sub-agent failed: session={subSession.SessionId}: {e.Message}");
            }
            finally
            {
                longRun.Cancel();
            }
        }
        finally
        {
            gate.Release();
        }

        // 交回在闸门外:只读这份 report 写父历史,与 gate 内的 runner 无关。
        // 排队轮可能在交回期间开跑,两者不再共享子会话执行者,无 runner 竞态。
        await DeliverAsync(subSession, parentId, report).ConfigureAwait(false);
    }

    private static async Task NoteLongRunAsync(string subSessionId, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(LongRunNotice, cancellationToken).ConfigureAwait(false);
            Notifier?.Invoke(ESubAgentNotice.LongRunning, subSessionId);
        }
        catch (OperationCanceledException)
        {
            //正常结束,不是事件
        }
    }

    /// <summary>
    /// 把回信送到派活者：它醒着就插进那一轮，闲着才落进历史再叫醒（ADR 0062）。
    ///
    /// 「还没回信」的标记在送到之后才清：插在队列里还没被取走时进程没了，启动扫描靠它把这封补上
    /// </summary>
    private static async Task DeliverAsync(ChatSession subSession, string parentId, string report)
    {
        // 同子会话的交回串行:轮次只串行 run,交回另起闸防止逆序——先跑完的那份必须先交回,
        // 不串行的话「晚跑完的先提交」会让更早那份的 ResolveSlot 命中历史尾部并原地替换掉新回信。
        // 只在交回期间持有,不占 _turnGates
        SemaphoreSlim deliverGate = _deliverGates.GetOrAdd(subSession.SessionId, _ => new SemaphoreSlim(1, 1));
        await deliverGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // 结论先落在子会话上:送到之前进程没了,启动扫描补的是这一份,而不是一句「被中止」
            subSession.PendingReport = report;
            subSession.SaveMeta(touchUpdatedAt: false);

            EDeliveryOutcome outcome = await SessionDelivery.Instance
                .DeliverAsync(new ReplyLetter(subSession, parentId, report),
                    () => MarkHandoffQueued(parentId, subSession.SessionId))
                .ConfigureAwait(false);
            // 交回与唤醒都留一句日志:「回信到了但界面没刷」这类问题才分得清是调度没跑还是界面没跟上
            Log.Debug($"Delivered background sub-agent reply: subSession={subSession.SessionId} outcome={outcome}");
        }
        catch (Exception e)
        {
            Log.Error($"Deliver background sub-agent reply failed: session={subSession.SessionId}: {e}");
        }
        finally
        {
            subSession.BackgroundReportPending = false;
            subSession.PendingReport = null;
            subSession.SaveMeta();
            ClearHandoffQueued(parentId, subSession.SessionId);
            RemovePendingTurn(parentId, subSession.SessionId);
            PendingWorkChanged?.Invoke(parentId);
            deliverGate.Release();
        }
    }

    /// <summary>
    /// 子代理的回信。交的是委派<b>自己攒出来的那份</b>，不是从子会话历史里现捞的最后一段正文——
    /// 那份带着「用户中止了」「超时了」「有几个调用没跑成」的注记，现捞会把它们全丢掉。
    /// 一跑起来就被停掉、什么都没产出的那种，靠 <c>interruption</c> 兜住：
    /// 「它没干成」本身就是派活者必须知道的事，静默等于让那条「已派出」永远没有下文
    /// </summary>
    private sealed class ReplyLetter(ChatSession subSession, string parentId, string report) : SessionLetter
    {
        private readonly string? _interruption = string.IsNullOrWhiteSpace(report) ? EmptyReport : null;

        public override string SessionId => parentId;

        public override string Cause => $"sub-agent reply {subSession.SessionId}";

        public override ChatMessage? Compose(ChatSession session) =>
            SubAgentReportHandoff.Compose(session, subSession, _interruption, report, OthersPending());

        public override ELetterWrite Write(ChatSession session) =>
            SubAgentReportHandoff.Write(session, subSession, _interruption, report, OthersPending()) switch
            {
                EHandoffOutcome.Appended => ELetterWrite.Written,
                // 原地替换成新的一封也要叫醒；一字未改的那种不必，但区分不出来，多叫一次无害
                EHandoffOutcome.Replaced => ELetterWrite.Written,
                _ => ELetterWrite.Missing,
            };

        // 数的是人不是轮次：同一位排着几轮也只算一位，这一位自己不算
        private int OthersPending() => Snapshot(parentId).Count(x => x != subSession.SessionId);
    }
}
