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
using UiharuMind.Core.AI.Execution.Assembly;
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
/// 它刻意<b>不认识子代理</b>——跑什么由调用方给一个委托，这里只管三件事：
/// 后台生命周期（含长跑提示）、报告交回、以及交回之后的<b>唤醒轮</b>。
///
/// 为什么要有唤醒轮，以及为什么它没有用户消息，见 ADR 0025。
/// </summary>
public static class BackgroundSubAgentDispatcher
{
    /// <summary>跑到这个时长还没完，弹一条提示把它顶到用户眼前。
    /// <c>SubAgentTool.Timeout</c>（24 小时）刻意没动，靠这条提示兜住「悄悄烧了一夜」</summary>
    public static readonly TimeSpan LongRunNotice = TimeSpan.FromMinutes(30);

    /// <summary>父会话正忙时唤醒的重试间隔。落盘早已完成，这里等的只是一个能起轮次的时机</summary>
    private static readonly TimeSpan WakeRetryInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 连续<b>无用户参与</b>的唤醒轮上限。掐的是自激空转（唤醒轮里又派后台子代理 → 又被唤醒），
    /// 不是总次数——用户说一句话即清零。形状同 <c>SubAgentTool.MaxDeniedApprovalRounds</c>。
    /// 取 32 而不是 12：主、子代理多轮讨论时每一轮都是无用户参与的唤醒轮，12 轮不够一次讨论收敛。
    /// </summary>
    private const int MaxConsecutiveWakeTurns = 32;

    // 父会话 → 名下未交回的子会话标识。记 id 而不是计数,是因为界面要按父会话问
    // 「名下有没有一个卡在审批上」——那件事只有子会话标识答得出
    private static readonly ConcurrentDictionary<string, HashSet<string>> _pendingByParent = new();

    // 反向索引:一张工具卡只知道自己那个子会话标识,要问的是「这一次委派交回了没有」。
    // 没有它就得扫遍所有父会话的集合,而每次运行态变化都要问一遍每张卡
    private static readonly ConcurrentDictionary<string, byte> _pendingSubSessions = new();
    private static readonly ConcurrentDictionary<string, int> _wakeStreakByParent = new();

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
    /// 唤醒轮的审批回应通道从哪儿取（按会话标识）。
    ///
    /// 由界面注入：唤醒轮跑的是<b>主代理</b> 那一轮，它要动东西时该弹给正看着它的人。
    /// 取不到就按无头口径拒绝——与 <c>InProcessSchedulerBackend.DenyUnauthorizedApprovals</c> 同形。
    /// </summary>
    public static Func<string, ApprovalResolver?>? WakeApprovalSource { get; set; }

    /// <summary>
    /// 需要让用户知道的事往哪儿送。由界面注入（<c>IMessageService</c> 在 App 项目，Core 引不到，
    /// 同 ADR 0021「为什么 Core 建 driver，不是 UI」）；无界面场合不设，调用处是空操作。
    /// </summary>
    public static Action<ESubAgentNotice, string>? Notifier { get; set; }

    /// <summary>这个会话名下还有没有未交回的后台委派（<b>未了结的工作</b>的一半，另一半是它自己在不在跑）</summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>有则 true</returns>
    public static bool HasPendingWork(string? sessionId) => PendingCount(sessionId) > 0;

    /// <summary>这个会话名下未交回的后台委派数（右栏面板显示用）</summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>条数</returns>
    public static int PendingCount(string? sessionId) => Snapshot(sessionId).Length;

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
        !string.IsNullOrEmpty(subSessionId) && _pendingSubSessions.ContainsKey(subSessionId);

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
    public static bool AnyPending() => !_pendingSubSessions.IsEmpty;

    /// <summary>进程内有没有<b>任何</b>后台委派卡在审批上等人点选</summary>
    public static bool AnyApprovalWaiting()
    {
        foreach (string subSessionId in _pendingSubSessions.Keys)
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
    /// 反复交回直到派活者闲下来接住。<b>不设上限</b>：报告是已经产出的结论，
    /// 除了「派活者一直在跑」没有别的理由交不成，而那件事总会结束。
    /// 排队期间在界面上是明说的（见 <see cref="HandoffQueued"/>）。
    /// </summary>
    /// <param name="parentId">派活者标识</param>
    /// <param name="subSessionId">子会话标识</param>
    /// <param name="submit">交回一次，返回结果</param>
    /// <returns>最后一次交回的结果</returns>
    private static async Task<EHandoffOutcome> SubmitWhenParentIdleAsync(string parentId, string subSessionId,
        Func<EHandoffOutcome> submit)
    {
        // 成功路径故意留一句日志:交回与唤醒都不再静默,否则"报告到了但界面没刷"这类问题
        // 无从区分是调度没跑还是界面没跟上(实机见过)。
        EHandoffOutcome outcome;
        int waits = 0;
        while ((outcome = submit()) == EHandoffOutcome.ParentBusy)
        {
            waits++;
            MarkHandoffQueued(parentId, subSessionId);
            await Task.Delay(WakeRetryInterval).ConfigureAwait(false);
        }

        Log.Debug($"Handed back background sub-agent report: subSession={subSessionId} "
                  + $"outcome={outcome} waited={waits}x{WakeRetryInterval.TotalSeconds:0}s");
        return outcome;
    }

    /// <summary>
    /// 用户在这个会话里说话了：自激封顶清零。
    ///
    /// 由 <see cref="TurnDriver.RunAsync"/> 在<b>带用户消息</b>的轮次上调——
    /// 「有没有用户参与」的唯一诚实判据就是这一轮有没有用户消息。
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    public static void NoteUserTurn(string? sessionId)
    {
        if (!string.IsNullOrEmpty(sessionId)) _wakeStreakByParent.TryRemove(sessionId, out _);
    }

    /// <summary>
    /// 把一次委派转入后台，<b>立即返回</b>。
    ///
    /// 调用方拿到的那句话会当场成为这次工具调用的结果（框架没有「挂起的工具结果」这种东西，
    /// 见 ADR 0025）。
    ///
    /// <b>它与 <c>AgentToolPrompts.SubAgentDefault</c> 分工，不要两边都写</b>：
    /// 提示词是每轮重发的固定开销，说的是<b>政策</b>（派不派、派哪一档、后台意味着什么、
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
        subSession.BackgroundReportPending = true;
        subSession.SaveMeta();
        HashSet<string> pending = _pendingByParent.GetOrAdd(parentId, _ => new HashSet<string>());
        lock (pending) pending.Add(subSession.SessionId);
        _pendingSubSessions[subSession.SessionId] = 0;
        PendingWorkChanged?.Invoke(parentId);

        _ = Task.Run(() => RunInBackgroundAsync(subSession, parentId, run));

        // 结尾那行 `[sub-session: …]` 是**跨模块的契约**,不是随手写的格式:
        // 回放历史时卡片靠它认出「这是一次委派」并挂出「查看过程」入口
        // (ToolCallItem.ParseSubSessionId 的正则),而 SubSessionStartedContent 那条
        // 随当时那一轮就消失了。报告用的是同一个格式,两种工具结果因此一致
        return "Dispatched to the background. "
               + "NO RESULT YET - it has not found or done anything at this point. "
               + "Its report arrives on its own; do not poll for it.\n"
               + (notice.Length > 0 ? notice + "\n" : string.Empty)
               + $"[sub-session: {subSession.SessionId}]";
    }

    /// <summary>
    /// 启动时收口：上次进程退出时还在跑的后台委派，父会话里那条「已派出」<b>永远等不到下文</b>。
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

                subSession.BackgroundReportPending = false;
                subSession.SaveMeta();
                SubAgentReportHandoff.Submit(subSession, "在应用退出时被中止，没有跑完");
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

        // 同一子会话一次只跑一轮(含交回):堆叠的 Continue 在这里排队,而不是各跑各的执行者。
        // 派活者不被阻塞——Dispatch 当场就返回了,等的是 detached 的后台任务。
        SemaphoreSlim gate = _turnGates.GetOrAdd(subSession.SessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            string report = string.Empty;
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

            await DeliverAsync(subSession, parentId, report).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
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
    /// 报告交回 + 唤醒。
    ///
    /// <b>这是两件事，不能缠成一件。</b>落盘不可失败；唤醒可以排队、可以被封顶掐掉、
    /// 最坏干脆不发生——那时报告仍然躺在历史里，模型下一轮自然读到。
    /// </summary>
    private static async Task DeliverAsync(ChatSession subSession, string parentId, string report)
    {
        EHandoffOutcome handoff = EHandoffOutcome.NothingToReport;
        try
        {
            // 父会话正在跑时写它的历史会与落盘交错,等到它闲下来。这也顺带实现了「合并」:
            // 排队期间跑完的其他委派各自 Submit 一条,最后只起一轮把它们一起交给模型
            handoff = await SubmitWhenParentIdleAsync(parentId, subSession.SessionId,
                () => SubmitReport(subSession, report)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Error($"Hand back background sub-agent report failed: session={subSession.SessionId}: {e}");
        }
        finally
        {
            subSession.BackgroundReportPending = false;
            subSession.SaveMeta();
            ClearHandoffQueued(parentId, subSession.SessionId);
            if (_pendingByParent.TryGetValue(parentId, out HashSet<string>? pending))
            {
                lock (pending) pending.Remove(subSession.SessionId);
            }

            _pendingSubSessions.TryRemove(subSession.SessionId, out _);

            PendingWorkChanged?.Invoke(parentId);
        }

        // 没新内容落盘（交不成、没结论）就别唤醒：空转一轮读不到新报告，
        // 还白烧一次无用户参与额度
        if (handoff is EHandoffOutcome.Appended or EHandoffOutcome.Replaced)
        {
            await WakeParentAsync(parentId).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 交回一次后台委派的报告。
    ///
    /// 交的是委派<b>自己攒出来的那份报告</b>，不是从子会话历史里现捞的最后一段正文——
    /// 那份带着「用户中止了」「超时了」「有几个调用没跑成」的注记，现捞会把它们全丢掉。
    /// 一跑起来就被停掉、什么都没产出的那种，靠 <c>interruption</c> 兜住：
    /// 「它没干成」本身就是派活者必须知道的事，静默等于让那条「已派出」永远没有下文。
    /// </summary>
    private static EHandoffOutcome SubmitReport(ChatSession subSession, string report) =>
        SubAgentReportHandoff.Submit(subSession,
            string.IsNullOrWhiteSpace(report) ? "没有产出任何结论就结束了" : null,
            report);

    private static async Task WakeParentAsync(string parentId)
    {
        if (string.IsNullOrEmpty(parentId)) return;
        //还有别的后台委派没回来:等最后那一个来起这一轮,省得一份报告一轮
        if (HasPendingWork(parentId))
        {
            Log.Debug($"Wake turn deferred: session={parentId} still has pending sub-sessions; "
                      + "the last one to finish wakes the parent.");
            return;
        }

        int streak = _wakeStreakByParent.AddOrUpdate(parentId, 1, (_, n) => n + 1);
        if (streak > MaxConsecutiveWakeTurns)
        {
            // 连转了这么多轮用户一句话没说,大概率是自激。报告已经落盘,不会丢——
            // 用户下次说话时模型自然读到
            Log.Warning($"Wake turn suppressed after {MaxConsecutiveWakeTurns} "
                        + $"consecutive turns without user input: session={parentId}");
            return;
        }

        try
        {
            ChatSession? parent = SessionManager.Instance.Load(parentId);
            if (parent == null) return;

            // 原子地占住这个会话:「查一下忙不忙,不忙就开跑」写成两步的话,查与开之间
            // 用户正好发一条,两轮就重叠了——它们共用会话本体、执行者与转录器。
            // 抢不到就不唤醒:报告已经在历史里,模型下一轮自然读到(落盘与唤醒本就是两件事)
            using IDisposable? claim = SessionManager.Instance.Running.TryBeginRun(parentId);
            if (claim == null)
            {
                Log.Debug($"Wake turn skipped: session={parentId} busy; report already in history.");
                return;
            }

            Log.Debug($"Wake turn started: session={parentId} streak={streak}");
            await parent.Runner.AttachAsync(parent).ConfigureAwait(false);
            using TurnDriver driver = new(null, new TurnUsageLedger());
            // 无头驱动(sink 为 null),但**审批有人接**——就是派活者自己那个窗口。
            // 这两件事必须分开告诉 TurnDriver:按 sink 推的话,共享的执行者会被标成
            // 「没人看着」,于是这一轮里派出的子代理连审批通道都不建(见 TurnDriver 的 attended)
            ApprovalResolver? resolver = WakeApprovalSource?.Invoke(parentId);
            // 没有用户消息:模型这一轮读的是刚落进历史的那条后续报告(见 ADR 0025)
            await driver.RunAsync(parent, parent.Runner, null, resolver, attended: resolver != null)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            //唤醒失败不影响结论:它已经在历史里了
            Log.Warning($"Wake turn failed: session={parentId}: {e.Message}");
        }
    }
}
