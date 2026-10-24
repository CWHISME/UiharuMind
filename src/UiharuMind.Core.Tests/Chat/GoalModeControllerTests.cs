using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Single.GoalMode;
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 单聊目标模式控制器：开启落注入并立刻开跑、轮末按调度排下次唤醒、没进展退避、goal.end 自判结束、
/// 手动结束。跑模型与等时间都换成假的：延迟只记下来，由测试决定什么时候到点；
/// 唤醒轮用假的（登记运行态→补回复→结束），StateChanged 广播让控制器接「一轮收场」
/// </summary>
public class GoalModeControllerTests
{
    private readonly List<(TimeSpan Delay, TaskCompletionSource Due)> _delays = [];
    private readonly List<(string SessionId, string Cause)> _wakeCalls = [];
    private readonly List<GoalModeReceipt> _receipts = [];
    private readonly List<CancellationToken> _wakeTokens = [];
    private TaskCompletionSource? _holdTurn; //非空时唤醒轮卡住，直到放行或被取消
    private readonly GoalModeController _controller;
    private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
    private string _stamp = "0";
    private bool _endOnNextTurn; //下一轮假装模型调 goal.end
    private string? _endReasonOverride; //goal.end 的 reason 覆盖（默认 done）

    public GoalModeControllerTests()
    {
        GoalModePersistence.Save([]); //清掉别的测试残留，避免 Restore 误恢复
        _controller = new GoalModeController(Delay, () => _now, _ => _stamp, WakeTurn, primary: true);
        _controller.Ended += receipt =>
        {
            lock (_receipts) _receipts.Add(receipt);
        };
    }

    [Fact]
    public async Task Start_InjectsTheGoal_AndImmediatelyRunsTheFirstTurn()
    {
        ChatSession session = NewSession();
        try
        {
            Assert.True(_controller.Start(session, new GoalModeSettings("写一份方案", [Interval30])));
            await Until(() => _wakeCalls.Count == 1);

            ChatMessage? injection = session.History.LastOrDefault(ChatMessageAnnotations.IsGoalModeInjection);
            Assert.NotNull(injection);
            Assert.Contains("写一份方案", injection.Text);
            Assert.Contains("goal.end", injection.Text);
        }
        finally
        {
            SessionManager.Instance.Delete(session.SessionId);
        }
    }

    /// <summary>注入必须走 SaveAppended 并把新增段广播出去：打开着的窗口靠 HistoryAppended
    /// 实时补画旁白，否则「唤醒的系统消息」要等那一轮跑完对账才出现</summary>
    [Fact]
    public async Task Inject_RaisesHistoryAppended_SoOpenWindowsRepaintImmediately()
    {
        ChatSession session = NewSession();
        try
        {
            List<int> appended = [];
            session.HistoryAppended += appended.Add;

            _controller.Start(session, new GoalModeSettings("写一份方案", [Interval30]));
            await Until(() => _wakeCalls.Count == 1);

            Assert.NotEmpty(appended);
            Assert.Single(session.History, ChatMessageAnnotations.IsGoalModeInjection);
        }
        finally
        {
            SessionManager.Instance.Delete(session.SessionId);
        }
    }

    [Fact]
    public async Task Start_WhileActive_ReturnsFalse()
    {
        ChatSession session = NewSession();
        try
        {
            _controller.Start(session, new GoalModeSettings("目标", [Interval30]));
            await Until(() => _wakeCalls.Count == 1);

            Assert.False(_controller.Start(session, new GoalModeSettings("另一个目标")));
        }
        finally
        {
            _controller.End(session.SessionId);
            SessionManager.Instance.Delete(session.SessionId);
        }
    }

    [Fact]
    public async Task TurnEnded_SchedulesTheIntervalWake_AndFiresOnDue()
    {
        ChatSession session = NewSession();
        try
        {
            _controller.Start(session, new GoalModeSettings("目标", [Interval30]));
            await Until(() => _wakeCalls.Count == 1); //首轮跑完
            await Until(() => _delays.Count == 1); //排上 Interval 30 分钟（首轮没新产物：再退避一段）
            Assert.Equal(TimeSpan.FromMinutes(30) + GoalModeWakePolicy.DefaultBackoffStart, _delays[0].Delay);

            _stamp = "1"; //第一轮落了新产物
            _delays[0].Due.SetResult(); //到点
            await Until(() => _wakeCalls.Count == 2); //再唤醒一轮
        }
        finally
        {
            _controller.End(session.SessionId);
            SessionManager.Instance.Delete(session.SessionId);
        }
    }

    [Fact]
    public async Task NoProgress_DelaysTheWakeByBackoff_ProgressResetsIt()
    {
        ChatSession session = NewSession();
        try
        {
            _controller.Start(session, new GoalModeSettings("目标", [Interval30]));
            await Until(() => _delays.Count == 1);
            //没进展（指纹没变）：调度时刻 30 分钟后，再退避一段（第一次无进展 = BackoffOf(0)）
            Assert.Equal(TimeSpan.FromMinutes(30) + GoalModeWakePolicy.DefaultBackoffStart, _delays[0].Delay);

            _stamp = "1"; //这一轮落了新产物
            _delays[0].Due.SetResult();
            await Until(() => _wakeCalls.Count == 2);
            await Until(() => _delays.Count == 2);
            Assert.Equal(TimeSpan.FromMinutes(30), _delays[1].Delay); //有进展：退避归零
        }
        finally
        {
            _controller.End(session.SessionId);
            SessionManager.Instance.Delete(session.SessionId);
        }
    }

    [Fact]
    public async Task GoalEnd_Done_EndsTheMode_WithTheSummary()
    {
        ChatSession session = NewSession();
        try
        {
            _endOnNextTurn = true;
            _controller.Start(session, new GoalModeSettings("目标", [Interval30]));
            await Until(() => _receipts.Count == 1);

            GoalModeReceipt receipt = _receipts[0];
            Assert.Equal(EGoalModeEndReason.Done, receipt.Reason);
            Assert.Equal("写完了", receipt.Summary);
            Assert.Equal(1, receipt.Turns);
            Assert.Null(_controller.StatusOf(session.SessionId)); //已摘掉
        }
        finally
        {
            SessionManager.Instance.Delete(session.SessionId);
        }
    }

    [Fact]
    public async Task GoalEnd_NeedsUser_EndsWithNeedsUserReason()
    {
        ChatSession session = NewSession();
        try
        {
            _endOnNextTurn = true;
            _endReasonOverride = "needs_user";
            _controller.Start(session, new GoalModeSettings("目标", [Interval30]));
            await Until(() => _receipts.Count == 1);

            Assert.Equal(EGoalModeEndReason.NeedsUser, _receipts[0].Reason);
            Assert.Equal("需要你拍板", _receipts[0].Summary);
        }
        finally
        {
            SessionManager.Instance.Delete(session.SessionId);
        }
    }

    [Fact]
    public async Task ManualEnd_EmitsReceipt_AndCancelsThePendingWake()
    {
        ChatSession session = NewSession();
        try
        {
            _controller.Start(session, new GoalModeSettings("目标", [Interval30]));
            await Until(() => _delays.Count == 1);
            await Until(() => _controller.StatusOf(session.SessionId)?.IsRunning == false);

            _controller.End(session.SessionId);
            await Until(() => _receipts.Count == 1);

            GoalModeReceipt receipt = _receipts[0];
            Assert.Equal(EGoalModeEndReason.Manual, receipt.Reason);
            Assert.Equal(1, receipt.Turns);
            //延迟唤醒已取消：到点不再叫
            _delays[0].Due.SetResult();
            await Task.Delay(50);
            Assert.Equal(1, _wakeCalls.Count);
        }
        finally
        {
            SessionManager.Instance.Delete(session.SessionId);
        }
    }

    [Fact]
    public async Task WakeNow_WhileWaiting_RunsAnotherTurn()
    {
        ChatSession session = NewSession();
        try
        {
            _controller.Start(session, new GoalModeSettings("目标", [Interval30]));
            await Until(() => _wakeCalls.Count == 1);
            await Until(() => _controller.StatusOf(session.SessionId)?.IsRunning == false);

            _controller.WakeNow(session.SessionId);
            await Until(() => _wakeCalls.Count == 2);
        }
        finally
        {
            _controller.End(session.SessionId);
            SessionManager.Instance.Delete(session.SessionId);
        }
    }

    [Fact]
    public async Task Update_Goal_DoesNotInject_NextWakeCarriesTheNewGoal()
    {
        ChatSession session = NewSession();
        try
        {
            _controller.Start(session, new GoalModeSettings("旧目标", [Interval30]));
            await Until(() => _wakeCalls.Count == 1);
            int injectionsAfterStart = session.History.Count(ChatMessageAnnotations.IsGoalModeInjection);

            Assert.True(_controller.Update(session.SessionId, new GoalModeSettings("新目标", AutoApproveApprovals: false)));
            // 改目标不落消息：注入条数不变
            Assert.Equal(injectionsAfterStart, session.History.Count(ChatMessageAnnotations.IsGoalModeInjection));

            // 下次唤醒（WakeNow）带的提醒里是当前目标
            await Until(() => _controller.StatusOf(session.SessionId)?.IsRunning == false);
            _controller.WakeNow(session.SessionId);
            await Until(() => _wakeCalls.Count == 2);
            ChatMessage? wake = session.History.LastOrDefault(x =>
                ChatMessageAnnotations.IsGoalModeInjection(x) && x.Text.Contains("当前目标"));
            Assert.NotNull(wake);
            Assert.Contains("新目标", wake.Text);
        }
        finally
        {
            _controller.End(session.SessionId);
            SessionManager.Instance.Delete(session.SessionId);
        }
    }

    [Fact]
    public async Task ManualEnd_WhileRunning_CancelsTheTurn_AndEmitsAfterItStops()
    {
        ChatSession session = NewSession();
        try
        {
            _holdTurn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _controller.Start(session, new GoalModeSettings("目标", [Interval30]));
            await Until(() => _wakeTokens.Count == 1);

            _controller.End(session.SessionId);
            Assert.True(_wakeTokens[0].IsCancellationRequested); //正在跑的那一轮收到取消
            await Until(() => _receipts.Count == 1);
            Assert.Equal(EGoalModeEndReason.Manual, _receipts[0].Reason);
        }
        finally
        {
            _holdTurn = null;
            SessionManager.Instance.Delete(session.SessionId);
        }
    }

    [Fact]
    public async Task UpdateRules_WhileWaiting_ReschedulesByTheNewRules()
    {
        ChatSession session = NewSession();
        try
        {
            _controller.Start(session, new GoalModeSettings("目标", [Interval30]));
            await Until(() => _delays.Count == 1);
            await Until(() => _controller.StatusOf(session.SessionId)?.IsRunning == false);

            Assert.True(_controller.Update(session.SessionId, new GoalModeSettings(null,
                    [new GoalModeScheduleRule(EGoalScheduleKind.Immediate)], false)));
            await Until(() => _delays.Count == 2);
            //首轮没进展：停即醒再退避一段
            Assert.Equal(GoalModeWakePolicy.DefaultBackoffStart, _delays[1].Delay);
            Assert.Equal(EGoalScheduleKind.Immediate, _controller.StatusOf(session.SessionId)!.Rules.Single().Kind);
        }
        finally
        {
            _controller.End(session.SessionId);
            SessionManager.Instance.Delete(session.SessionId);
        }
    }

    [Fact]
    public void NotOwner_RefusesToStart()
    {
        ChatSession session = NewSession();
        try
        {
            GoalModeController secondary = new(Delay, () => _now, _ => _stamp, WakeTurn, primary: false);
            Assert.False(secondary.IsOwner);
            Assert.False(secondary.Start(session, new GoalModeSettings("目标", [Interval30])));
            Assert.Empty(_wakeCalls);
        }
        finally
        {
            SessionManager.Instance.Delete(session.SessionId);
        }
    }

    [Fact]
    public async Task Start_WithAutoApprove_KeepsTheFlag_AndRestoresItAfterRestart()
    {
        ChatSession session = NewSession();
        try
        {
            Assert.True(_controller.Start(session, new GoalModeSettings("目标", [Interval30], AutoApproveApprovals: true)));
            await Until(() => _wakeCalls.Count == 1);

            Assert.True(_controller.StatusOf(session.SessionId)!.AutoApproveApprovals);
            Assert.True(_controller.IsAutoApprove(session.SessionId));
            Assert.False(_controller.IsAutoApprove("不存在的会话"));

            // 重启后恢复：勾选随存档回来（无人值守的那一轮照样自动同意）
            GoalModeController restored = new(Delay, () => _now, _ => _stamp, WakeTurn, primary: true);
            Assert.True(restored.IsAutoApprove(session.SessionId));
            Assert.True(restored.StatusOf(session.SessionId)!.AutoApproveApprovals);
            restored.End(session.SessionId);
        }
        finally
        {
            _controller.End(session.SessionId);
            SessionManager.Instance.Delete(session.SessionId);
        }
    }

    [Fact]
    public async Task Update_TogglesAutoApprove()
    {
        ChatSession session = NewSession();
        try
        {
            _controller.Start(session, new GoalModeSettings("目标", [Interval30]));
            await Until(() => _wakeCalls.Count == 1);
            Assert.False(_controller.IsAutoApprove(session.SessionId));

            Assert.True(_controller.Update(session.SessionId, new GoalModeSettings(AutoApproveApprovals: true)));
            Assert.True(_controller.IsAutoApprove(session.SessionId));

            Assert.True(_controller.Update(session.SessionId, new GoalModeSettings(AutoApproveApprovals: false)));
            Assert.False(_controller.IsAutoApprove(session.SessionId));
        }
        finally
        {
            _controller.End(session.SessionId);
            SessionManager.Instance.Delete(session.SessionId);
        }
    }

    /// <summary>局部更新：只改目标时自动同意保持原值，不会被缺省覆写</summary>
    [Fact]
    public async Task Update_OnlyGoal_LeavesAutoApproveAlone()
    {
        ChatSession session = NewSession();
        try
        {
            _controller.Start(session, new GoalModeSettings("目标", [Interval30], AutoApproveApprovals: true));
            await Until(() => _wakeCalls.Count == 1);

            Assert.True(_controller.Update(session.SessionId, new GoalModeSettings("新目标")));
            Assert.Equal("新目标", _controller.StatusOf(session.SessionId)!.Goal);
            Assert.True(_controller.IsAutoApprove(session.SessionId));
        }
        finally
        {
            _controller.End(session.SessionId);
            SessionManager.Instance.Delete(session.SessionId);
        }
    }

    private static GoalModeScheduleRule Interval30 => new(EGoalScheduleKind.Interval, Interval: TimeSpan.FromMinutes(30));

    private ChatSession NewSession() => SessionManager.Instance.StartNewSession(new CharacterData { CharacterName = "目标会话" });

    // 模拟跑一轮：登记运行态、补一条模型回复（可选带 goal.end 调用）、结束——
    // StateChanged 广播让控制器接「一轮收场」再排下次唤醒
    private async Task WakeTurn(string sessionId, string cause, CancellationToken token)
    {
        _wakeCalls.Add((sessionId, cause));
        _wakeTokens.Add(token);
        if (_holdTurn is { } hold)
        {
            await hold.Task.WaitAsync(token).ContinueWith(_ => { });
            if (token.IsCancellationRequested) return; //被结束取消：这一轮没跑成
        }
        ChatSession? session = SessionManager.Instance.Load(sessionId);
        using (SessionManager.Instance.Running.BeginRun(sessionId))
        {
            if (session != null)
            {
                if (_endOnNextTurn)
                {
                    session.History.Add(EndGoalCall(_endReasonOverride ?? "done"));
                    session.Save();
                }
                else
                {
                    session.AddGeneratedAssistantMessage("继续推进目标");
                }
            }
        }

        _endOnNextTurn = false;
        _endReasonOverride = null;
    }

    // 模型调 ExCall name="goal.end" 的历史形态（FindEndRequest 从历史里认它）
    private static ChatMessage EndGoalCall(string reason) => new(ChatRole.Assistant,
        [new FunctionCallContent("goal-end-call", ExtensionCalls.CallName,
            new Dictionary<string, object?>
            {
                ["name"] = "goal.end",
                ["args"] = new Dictionary<string, object?>
                {
                    ["reason"] = reason,
                    ["summary"] = reason == "needs_user" ? "需要你拍板" : "写完了",
                },
            })]);

    private Task Delay(TimeSpan delay, CancellationToken token)
    {
        TaskCompletionSource due = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _delays.Add((delay, due));
        return due.Task;
    }

    private static async Task Until(Func<bool> condition, int timeoutMs = 5000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not met");
            await Task.Delay(10);
        }
    }
}
