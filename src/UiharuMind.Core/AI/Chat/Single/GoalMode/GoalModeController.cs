using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Chat.WakeLoops;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.Core.Instances;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Chat.Single.GoalMode;

/// <summary>
/// 单聊目标模式：给一个 agent 会话设目标，模型自动停下来后按调度规则自动唤醒继续推，
/// 直到模型自判结束（调隐藏扩展 goal.end）或用户手动结束。
///
/// <list type="bullet">
/// <item>无保险丝：模型自判结束或用户手动结束是仅有的两条出路（运行期不设时长/轮数上限）</item>
/// <item>没进展（产物指纹没变）不结束，只把下次唤醒再退避一段：逐次翻倍、封顶，一有进展就归零</item>
/// <item>调度规则可多条并存：Daily / OneShot / Interval / Immediate 各自算下次触发时刻，取最近；OneShot 触发后作废</item>
/// <item>无人值守的后台活只归主实例（ADR 0064）：非主实例不恢复、不落盘、开不了</item>
/// </list>
/// 一轮结束的信号从 <see cref="SessionManager.Running"/>（<see cref="SessionRunRegistry.StateChanged"/>）取；
/// 唤醒轮复用 <see cref="SessionWakeTurn.RunAsync"/>（无用户消息、审批弹回会话窗口），结束即取消
/// </summary>
public sealed class GoalModeController
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<ChatSession, string> _artifactStamp;
    private readonly Func<string, string, CancellationToken, Task> _wakeTurn;
    private readonly object _sync = new();
    private readonly Dictionary<string, GoalModeSession> _sessions = new(); //会话 → 进行中的目标模式
    private readonly Dictionary<string, GoalModeSettings> _lastSettings = new(); //会话 → 最近一次设置（结束不删，Setup 弹窗回填用）

    /// <summary>应用里的那一个</summary>
    public static GoalModeController Instance { get; } = CreateInstance();

    /// <summary>某个会话的目标模式状态变了（开始、开跑/跑完、延迟唤醒排上/取消、结束）。⚠️ 可能来自后台线程</summary>
    public event Action<string>? StatusChanged;

    /// <summary>一次目标模式结束了，交出回执。⚠️ 可能来自后台线程</summary>
    public event Action<GoalModeReceipt>? Ended;

    /// <summary>
    /// 构造（测试用来换掉时间、指纹与唤醒执行者）
    /// </summary>
    /// <param name="delay">等一段时间；null 走 <see cref="Task.Delay(TimeSpan, CancellationToken)"/></param>
    /// <param name="now">当前时刻；null 取本地时间</param>
    /// <param name="artifactStamp">会话产物指纹（变了即有新产物）；null 按件数+最近改动算</param>
    /// <param name="wakeTurn">让会话再跑一轮；null 走 <see cref="SessionWakeTurn.RunAsync"/></param>
    /// <param name="primary">是否主实例；null 取 <see cref="AppInstance.IsPrimary"/></param>
    public GoalModeController(Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DateTimeOffset>? now = null, Func<ChatSession, string>? artifactStamp = null,
        Func<string, string, CancellationToken, Task>? wakeTurn = null, bool? primary = null)
    {
        _delay = delay ?? Task.Delay;
        _now = now ?? (() => DateTimeOffset.Now);
        _artifactStamp = artifactStamp ?? (session => WakeLoop.StampOf(GroupArtifacts.CollectForSession(session)));
        _wakeTurn = wakeTurn ?? SessionWakeTurn.RunAsync;
        IsOwner = primary ?? AppInstance.IsPrimary;
        if (!IsOwner)
        {
            Log.Debug("Goal mode is owned by another instance; this one does not run it.");
            return;
        }

        SessionManager.Instance.Running.StateChanged += OnStateChanged;
        Restore();
    }

    /// <summary>本实例管不管目标模式：非主实例为 false（ADR 0064），界面据此不让开</summary>
    public bool IsOwner { get; }

    /// <summary>某个会话此刻的目标模式状态；没在为 null</summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>状态；没在目标模式为 null</returns>
    public GoalModeStatus? StatusOf(string sessionId)
    {
        lock (_sync) return _sessions.GetValueOrDefault(sessionId)?.Snapshot();
    }

    /// <summary>
    /// 某个会话最近一次的目标模式设置（目标、调度规则、自动同意审批）。结束目标模式不删：
    /// 设置弹窗在没进行中状态时回填它，用户设过的东西结束后仍看得到
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>最近设置；从没开过目标模式为 null</returns>
    public GoalModeSettings? LastSettingsOf(string sessionId)
    {
        lock (_sync) return _lastSettings.GetValueOrDefault(sessionId);
    }

    /// <summary>某个会话是不是在目标模式里（SessionWakeTurn 的封顶豁免据此放行）</summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>在为 true</returns>
    public bool IsActive(string sessionId)
    {
        lock (_sync) return _sessions.ContainsKey(sessionId);
    }

    /// <summary>
    /// 某个会话的目标模式是不是勾了自动同意审批：这个会话的审批一条都不问（唤醒轮与用户自己那一轮
    /// 都一样），没窗口那头按同意收口、有窗口那头把卡片收成已同意。⚠️ 可能来自后台线程
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>勾了为 true</returns>
    public bool IsAutoApprove(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return false;
        lock (_sync) return _sessions.TryGetValue(sessionId, out GoalModeSession? session) && session.AutoApproveApprovals;
    }

    /// <summary>
    /// 这个会话勾了自动同意审批时，给出无人值守的审批通道；没勾为 null。
    ///
    /// 「问不问人」只此一处说了算：会话窗口与唤醒轮的无人值守策略都向它问（见
    /// <c>Features.Conversation.WakeApprovalHosts</c>），两边不会各判一套。
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>审批通道；没勾为 null</returns>
    public ApprovalResolver? ResolverFor(string? sessionId) =>
        IsAutoApprove(sessionId) ? GoalModeApproval.Create(sessionId!) : null;

    /// <summary>全部进行中的目标模式（统一清单用）</summary>
    /// <returns>状态列表</returns>
    public IReadOnlyList<GoalModeStatus> ActiveStatuses()
    {
        lock (_sync) return _sessions.Values.Select(x => x.Snapshot()).ToList();
    }

    /// <summary>
    /// 开始目标模式：落目标注入、立刻跑第一轮（模型正在跑就不抢，那轮结束后按调度接）
    /// </summary>
    /// <param name="session">会话（agent 档）</param>
    /// <param name="settings">目标与调度规则、自动同意审批</param>
    /// <returns>开始了为 true；已经在目标模式或本实例不管目标模式为 false</returns>
    public bool Start(ChatSession session, GoalModeSettings settings)
    {
        if (!IsOwner) return false;
        GoalModeSession created;
        lock (_sync)
        {
            if (_sessions.ContainsKey(session.SessionId)) return false;
            created = new GoalModeSession(session, settings.Goal, EffectiveRules(settings.Rules), _now(),
                WakeLoop.SafeStamp(_artifactStamp, session), settings.AutoApproveApprovals ?? false);
            _sessions[session.SessionId] = created;
            _lastSettings[session.SessionId] = new GoalModeSettings(created.Goal, created.Rules, created.AutoApproveApprovals);
        }

        Inject(session, GoalModeTranscript.KickoffNote(created.Goal));
        Save();
        RaiseStatusChanged(session.SessionId);
        Wake(created);
        return true;
    }

    /// <summary>
    /// 更新目标、调度规则与自动同意。目标改动不落消息：模型在下次唤醒时经带当前目标的提醒读到。
    /// 规则改了且没在跑就按新规则重排下次唤醒；正在跑的，那一轮收场时按新规则排
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="settings">新参数；<see cref="GoalModeSettings.Goal"/> 为 null 表示不改</param>
    /// <returns>更新了为 true；没在目标模式为 false</returns>
    public bool Update(string sessionId, GoalModeSettings settings)
    {
        GoalModeSession? session;
        bool reschedule;
        lock (_sync)
        {
            session = _sessions.GetValueOrDefault(sessionId);
            if (session == null) return false;
            reschedule = session.Update(settings.Goal, settings.Rules == null ? null : EffectiveRules(settings.Rules),
                          settings.AutoApproveApprovals)
                          && !session.IsRunning;
            // 更新后的实际生效值也记进最近设置（空规则被换成默认循环等，弹窗回填显示生效的那条）
            _lastSettings[sessionId] = new GoalModeSettings(session.Goal, session.Rules, session.AutoApproveApprovals);
        }

        Save();
        if (reschedule) ScheduleNext(session);
        else RaiseStatusChanged(sessionId);
        return true;
    }

    /// <summary>不等调度，立刻唤醒。会话正忙就不叫</summary>
    /// <param name="sessionId">会话标识</param>
    public void WakeNow(string sessionId)
    {
        GoalModeSession? session;
        lock (_sync) session = _sessions.GetValueOrDefault(sessionId);
        if (session == null || SessionManager.Instance.Running.IsBusy(sessionId)) return;
        Wake(session);
    }

    /// <summary>用户手动结束。目标模式唤醒的那一轮正在跑就随之停下</summary>
    /// <param name="sessionId">会话标识</param>
    public void End(string sessionId)
    {
        GoalModeSession? session;
        lock (_sync) session = _sessions.GetValueOrDefault(sessionId);
        if (session != null) Finish(session, EGoalModeEndReason.Manual, string.Empty);
    }

    private static GoalModeController CreateInstance() => new();

    private static IReadOnlyList<GoalModeScheduleRule> EffectiveRules(IReadOnlyList<GoalModeScheduleRule>? rules) =>
        rules is { Count: > 0 } ? rules : [GoalModeScheduleRule.Default];

    // 一轮收场（从非空闲回到空闲）：不论是唤醒轮还是用户自己发的那一轮，都从这里接着排下一次唤醒
    private void OnStateChanged(string sessionId)
    {
        GoalModeSession? session;
        bool ended = false;
        lock (_sync)
        {
            session = _sessions.GetValueOrDefault(sessionId);
            if (session == null) return;
            ESessionRunState state = SessionManager.Instance.Running.StateOf(sessionId);
            // NotifyStateChanged 这类不改变状态的广播滤掉
            if (state == ESessionRunState.Idle && session.LastRunState != ESessionRunState.Idle) ended = true;
            session.LastRunState = state;
        }

        if (ended) OnTurnEnded(session!);
    }

    private void OnTurnEnded(GoalModeSession session)
    {
        ChatSession chat = session.Session;
        // 模型自判结束：这一轮里调了 goal.end，认出即收场
        if (FindEndCall(chat, session.HistoryStart) is { } end)
        {
            Finish(session, end.Reason, end.Summary);
            return;
        }

        string stamp = WakeLoop.SafeStamp(_artifactStamp, chat);
        lock (_sync)
        {
            if (session.IsEnded) return;
            session.BackoffLevel = stamp != session.ArtifactStamp ? 0 : session.BackoffLevel + 1;
            session.ArtifactStamp = stamp;
            session.LastStop = _now();
        }

        Save(); //重启后 Interval 从真实的停下时刻起算
        ScheduleNext(session);
    }

    // 按规则排下次唤醒；没有可触发的规则就保持待命（可手动唤醒或结束）
    private void ScheduleNext(GoalModeSession session, int? idleStreak = null)
    {
        DateTimeOffset? wakeAt;
        CancellationToken token = default;
        lock (_sync)
        {
            if (session.IsEnded) return;
            wakeAt = GoalModeWakePolicy.NextWake(session.Rules, _now(), session.LastStop,
                idleStreak ?? session.BackoffLevel);
            if (wakeAt == null) session.CancelDelay();
            else token = session.BeginDelay(wakeAt.Value);
        }

        RaiseStatusChanged(session.Session.SessionId);
        if (wakeAt != null) DelayThenWakeAsync(session, wakeAt.Value - _now(), token).LogOnFault("wake the goal mode session later");
    }

    private async Task DelayThenWakeAsync(GoalModeSession session, TimeSpan delay, CancellationToken token)
    {
        if (!await WakeLoop.WaitAsync(_delay, delay, token).ConfigureAwait(false)) return;
        lock (_sync)
        {
            if (!session.TryTakeDue(token)) return;
        }

        Wake(session);
    }

    private void Wake(GoalModeSession session)
    {
        int turns;
        CancellationToken token;
        lock (_sync)
        {
            if (session.IsEnded || session.IsRunning) return;
            session.CancelDelay();
            session.IsRunning = true;
            turns = ++session.Turns;
            token = session.Token;
        }

        // 每次唤醒（首轮除外）落一条带当前目标的提醒：改目标不落消息，靠这一句让模型以最新为准
        if (turns > 1) Inject(session.Session, GoalModeTranscript.WakeNote(session.Goal));
        RaiseStatusChanged(session.Session.SessionId);
        RunTurnAsync(session, token).LogOnFault("run a goal mode turn");
    }

    private async Task RunTurnAsync(GoalModeSession session, CancellationToken token)
    {
        string sessionId = session.Session.SessionId;
        try
        {
            await _wakeTurn(sessionId, "goal-mode", token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Warning($"Goal mode turn failed: session={sessionId}: {e.Message}");
        }

        bool ended;
        lock (_sync)
        {
            session.IsRunning = false;
            ended = session.IsEnded;
        }

        // 跑着时结束了：Finish 没出回执，停稳后在这里出
        if (ended) Emit(session);
        RaiseStatusChanged(sessionId);
    }

    // 标记结束、摘掉登记、落存档；正在跑就等那一轮停稳再出回执（RunTurnAsync 收尾补出）
    private void Finish(GoalModeSession session, EGoalModeEndReason reason, string summary)
    {
        bool running;
        lock (_sync)
        {
            if (session.IsEnded) return;
            session.End(reason, summary);
            _sessions.Remove(session.Session.SessionId);
            running = session.IsRunning;
        }

        Save();
        RaiseStatusChanged(session.Session.SessionId);
        if (!running) Emit(session);
    }

    // 出回执：交给界面（统一清单画卡），历史不另写
    private void Emit(GoalModeSession session)
    {
        GoalModeReceipt receipt = new(session.Session.SessionId, session.EndReason!.Value, session.EndSummary,
            session.StartedAt, _now(), session.Turns);
        session.Dispose();
        WakeLoop.Raise(Ended, receipt, "Goal mode ended");
    }

    // 落一条目标模式注入（User 角色 + 旁白标注）：模型读历史看到，UI 画成旁白。
    // 用 SaveAppended 而不是 Save()：Save 不触发 HistoryAppended，打开着的窗口收不到信号，
    // 旁白要等那一轮跑完对账才补出来——用户看到的就是「唤醒的提醒要等轮完才出现」
    private static void Inject(ChatSession session, string text)
    {
        ChatMessage message = session.CreateMessage(ChatRole.User, text);
        ChatMessageAnnotations.MarkGoalModeInjection(message);
        int from = session.History.Count;
        session.History.Add(message);
        session.SaveAppended(from);
    }

    // 从一段历史里认 goal.end：模型经 ExCall 调用，拆开后是内层的 EndGoalMode
    private static GoalModeEndCall? FindEndCall(ChatSession session, int fromIndex)
    {
        for (int i = fromIndex; i < session.History.Count; i++)
        {
            foreach (AIContent content in session.History[i].Contents)
            {
                if (content is FunctionCallContent call && EndGoalModeTool.Read(ExtensionCalls.Unwrap(call)) is { } end)
                    return end;
            }
        }

        return null;
    }

    // 重启后恢复：登记进行中的目标模式，重排下一次唤醒（该触发没触发的，到点立即补跑）
    private void Restore()
    {
        List<GoalModeSession> restored = [];
        foreach (GoalModeState state in GoalModePersistence.Load())
        {
            ChatSession? session = SessionManager.Instance.Load(state.SessionId);
            if (session == null) continue; //会话没了：存档随下一次 Save 自然清掉
            lock (_sync)
            {
                if (_sessions.ContainsKey(state.SessionId)) continue;
                GoalModeSession goal = GoalModeSession.Restore(session, state);
                _sessions[state.SessionId] = goal;
                restored.Add(goal);
            }
        }

        // 重启后第一轮按有进展排：不继承存档时的退避级数继续放缓
        foreach (GoalModeSession goal in restored) ScheduleNext(goal, idleStreak: 0);
    }

    private void Save()
    {
        if (!IsOwner) return;
        List<GoalModeState> states;
        lock (_sync) states = _sessions.Values.Select(x => x.ToState()).ToList();
        GoalModePersistence.Save(states);
    }

    private void RaiseStatusChanged(string sessionId) => WakeLoop.Raise(StatusChanged, sessionId, "Goal mode status");
}
