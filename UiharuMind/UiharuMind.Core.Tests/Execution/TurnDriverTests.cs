using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.ToolCall;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 一轮对话的编排。这些事实过去只存在于界面的 ViewModel 里，全靠手测——
/// 「取消保留半截回复」「取消不留孤儿 tool_call」「审批多轮回环」「本轮没响应就不压缩」
/// 都是修过的 bug，这里把它们钉住。
///
/// 会话一律建成 <see cref="ChatSession.IsTransient"/>：<c>SessionManager</c> 的
/// <c>Save</c>/<c>SaveMeta</c>/<c>Append</c> 都在开头短路，因此整条持久化路径是空操作，
/// 不需要为测试开任何缝。（运行态登记仍会初始化 <c>SessionManager</c> 单例，
/// 那只是读一次索引，不写盘。）
/// </summary>
public class TurnDriverTests
{
    private const string CancelNote = ToolCallCancellation.ResultText;

    /// <summary>
    /// 会话一律带上一份现造的角色。这样 <c>ChatSession.CharacterData</c> 的缓存字段当场就位，
    /// 取用方不会去问全局角色库——那个库被另外三个测试类并行地 <c>OnInitialize()</c> 重建，
    /// 读到重建中的那一份会随机抛异常（实测偶发约六分之一）。
    /// 空的 <c>FirstGreeting</c> 意味着这个构造不会往历史里塞开场白。
    /// </summary>
    private static ChatSession NewSession()
    {
        return new ChatSession("test", new CharacterData { CharacterId = "test" }) { IsTransient = true };
    }

    private static ChatMessage Prompt(string text = "开工") => new(ChatRole.User, text);

    private static ChatMessage AssistantCall(string callId)
    {
        return new ChatMessage(ChatRole.Assistant,
            new List<AIContent> { new FunctionCallContent(callId, "Shell", null) });
    }

    private static UsageContent Usage(long input, long output)
    {
        return new UsageContent(new UsageDetails { InputTokenCount = input, OutputTokenCount = output });
    }

    /// <summary>一段内容流：吐完给定内容后当场被掐断（等价于取消标记在枚举中途生效）</summary>
    private static Func<IEnumerable<AIContent>> Interrupted(params AIContent[] before)
    {
        return () => InterruptedCore(before);
    }

    private static IEnumerable<AIContent> InterruptedCore(AIContent[] before)
    {
        foreach (AIContent content in before) yield return content;
        throw new OperationCanceledException();
    }

    private static Func<IEnumerable<AIContent>> Round(params AIContent[] contents) => () => contents;

    private static Func<IEnumerable<AIContent>> Failing(string message)
    {
        return () => throw new InvalidOperationException(message);
    }

    //================= 内容流与收尾 =================

    [Fact]
    public async Task EveryContent_ReachesTheSink()
    {
        FakeSink sink = new();
        StubRunner runner = new(Round(new TextContent("你"), new TextContent("好")));
        TurnDriver driver = new(sink, new TurnUsageLedger());

        await driver.RunAsync(NewSession(), runner, Prompt());

        Assert.Equal(["你", "好"], sink.Applied.OfType<TextContent>().Select(x => x.Text));
    }

    [Fact]
    public async Task Turn_ReportsStartedRoundPersistedThenEnded()
    {
        List<TurnNotice> notices = new();
        StubRunner runner = new(Round(new TextContent("嗯")));
        TurnDriver driver = new(new FakeSink(), new TurnUsageLedger(), notices.Add);

        await driver.RunAsync(NewSession(), runner, Prompt());

        Assert.Equal(
        [
            ETurnNotice.Started, ETurnNotice.RoundCompleted, ETurnNotice.Persisted, ETurnNotice.Ended,
        ], notices.Select(x => x.Kind));
    }

    [Fact]
    public async Task Turn_SavesRunnerState()
    {
        StubRunner runner = new(Round(new TextContent("嗯")));
        TurnDriver driver = new(new FakeSink(), new TurnUsageLedger());

        await driver.RunAsync(NewSession(), runner, Prompt());

        Assert.Equal(1, runner.SaveStateCalls);
    }

    [Fact]
    public async Task Finally_StopsRunningToolCallsWithTheCancellationNote()
    {
        //中途停止或出错时那条工具结果永远不会来,卡片会一直转圈——收尾必须无条件收掉它
        FakeSink sink = new();
        StubRunner runner = new(Interrupted(new TextContent("跑到一半")));
        TurnDriver driver = new(sink, new TurnUsageLedger());

        await driver.RunAsync(NewSession(), runner, Prompt());

        Assert.Equal([CancelNote], sink.StopNotes);
        Assert.Equal(1, sink.CloseNestedActivityCalls);
    }

    [Fact]
    public async Task NormalTurn_AlsoRunsTheSameFinally()
    {
        //正常结束时本就没有还在跑的调用,这里是空操作——但它必须照样被调,否则出错路径就漏了
        FakeSink sink = new();
        StubRunner runner = new(Round(new TextContent("说完了")));
        TurnDriver driver = new(sink, new TurnUsageLedger());

        await driver.RunAsync(NewSession(), runner, Prompt());

        Assert.Equal([CancelNote], sink.StopNotes);
        Assert.Equal(1, sink.CloseNestedActivityCalls);
    }

    //================= 审批回环 =================

    [Fact]
    public async Task Approval_ResponsesBecomeTheNextRoundInput()
    {
        ToolApprovalRequestContent request = new("req-1", new FunctionCallContent("c1", "Shell", null));
        StubRunner runner = new(Round(request), Round(new TextContent("跑完了")));
        ChatMessage response = new(ChatRole.User, "approved");
        TurnDriver driver = new(new FakeSink(), new TurnUsageLedger());

        await driver.RunAsync(NewSession(), runner, Prompt(), requests =>
        {
            Assert.Equal([request], requests);
            return Task.FromResult<IReadOnlyList<ChatMessage>>([response]);
        });

        Assert.Equal(2, runner.ReceivedRounds.Count);
        Assert.Equal([response], runner.ReceivedRounds[1]);
    }

    [Fact]
    public async Task Approval_WithoutResolver_EndsTheTurn()
    {
        //无人可回应时不能静默挂起等待
        ToolApprovalRequestContent request = new("req-1", new FunctionCallContent("c1", "Shell", null));
        StubRunner runner = new(Round(request), Round(new TextContent("不该跑到这里")));
        TurnDriver driver = new(new FakeSink(), new TurnUsageLedger());

        await driver.RunAsync(NewSession(), runner, Prompt(), resolver: null);

        Assert.Single(runner.ReceivedRounds);
    }

    [Fact]
    public async Task Approval_ResolverReturningNothing_EndsTheTurn()
    {
        //无头执行的轮次上限就靠这个收工:回应给空,循环结束
        ToolApprovalRequestContent request = new("req-1", new FunctionCallContent("c1", "Shell", null));
        StubRunner runner = new(Round(request), Round(new TextContent("不该跑到这里")));
        TurnDriver driver = new(new FakeSink(), new TurnUsageLedger());

        await driver.RunAsync(NewSession(), runner, Prompt(),
            _ => Task.FromResult<IReadOnlyList<ChatMessage>>([]));

        Assert.Single(runner.ReceivedRounds);
    }

    [Fact]
    public async Task NoApproval_EndsAfterOneRound()
    {
        StubRunner runner = new(Round(new TextContent("说完了")), Round(new TextContent("不该跑到这里")));
        TurnDriver driver = new(new FakeSink(), new TurnUsageLedger());

        await driver.RunAsync(NewSession(), runner, Prompt(),
            _ => Task.FromResult<IReadOnlyList<ChatMessage>>([new ChatMessage(ChatRole.User, "x")]));

        Assert.Single(runner.ReceivedRounds);
    }

    //================= 取消收尾 =================

    [Fact]
    public async Task Cancelled_ClosesUnansweredToolCallsBeforeAppendingText()
    {
        //结果必须紧跟在它那条调用之后,顺序反了历史就对不上
        ChatSession session = NewSession();
        session.History.Add(Prompt());
        session.History.Add(AssistantCall("c1"));

        FakeSink sink = new("半截回复");
        StubRunner runner = new(Interrupted(new TextContent("半截回复")));
        TurnDriver driver = new(sink, new TurnUsageLedger());

        await driver.RunAsync(session, runner, Prompt());

        FunctionResultContent result = Assert.IsType<FunctionResultContent>(
            Assert.Single(session.History[2].Contents));
        Assert.Equal(ChatRole.Tool, session.History[2].Role);
        Assert.Equal("c1", result.CallId);
        Assert.True(ToolCallCancellation.IsCancelled(result));

        Assert.Equal(ChatRole.Assistant, session.History[3].Role);
        Assert.Equal("半截回复", session.History[3].Text);
    }

    [Fact]
    public async Task Cancelled_WithNothingStreaming_AppendsNoAssistantMessage()
    {
        //卡在工具调用上被停掉:没有正文可留,不能凭空写一条空消息
        ChatSession session = NewSession();
        session.History.Add(Prompt());

        StubRunner runner = new(Interrupted());
        TurnDriver driver = new(new FakeSink(), new TurnUsageLedger()); //没有正文可取

        await driver.RunAsync(session, runner, Prompt());

        Assert.Single(session.History);
    }

    [Fact]
    public async Task Cancelled_DoesNotReportFailure()
    {
        //用户主动停止不是错误,界面上不该冒出一条错误条目
        List<TurnNotice> notices = new();
        StubRunner runner = new(Interrupted(new TextContent("半截")));
        TurnDriver driver = new(new FakeSink("半截"), new TurnUsageLedger(), notices.Add);

        await driver.RunAsync(NewSession(), runner, Prompt());

        string? failure = notices.Where(x => x.Kind == ETurnNotice.Failed)
            .Select(x => x.Payload ?? "(无正文)")
            .FirstOrDefault();
        Assert.True(failure == null, $"用户主动停止不该报失败,却报了:{failure}");
    }

    [Fact]
    public async Task Failure_ReportsFailedNoticeCarryingTheMessage()
    {
        List<TurnNotice> notices = new();
        StubRunner runner = new(Failing("连接断了"));
        TurnDriver driver = new(new FakeSink(), new TurnUsageLedger(), notices.Add);

        await driver.RunAsync(NewSession(), runner, Prompt());

        TurnNotice failed = Assert.Single(notices, x => x.Kind == ETurnNotice.Failed);
        Assert.Equal("连接断了", failed.Payload);
    }

    //================= 退出收尾 =================

    [Fact]
    public async Task SettleForShutdown_MidTurn_WritesResultsAndHalfReplyExactlyOnce()
    {
        //进程马上就没了,运行循环等不到观察取消的那一刻,所以要当场补写;
        //而随后取消分支还会再走一次收尾,两次加起来只能落一份
        ChatSession session = NewSession();
        session.History.Add(AssistantCall("c1"));

        FakeSink sink = new("半截回复");
        StubRunner runner = new(Interrupted(new TextContent("半截回复"), new TextContent("再一段")));
        TurnDriver driver = new(sink, new TurnUsageLedger());
        sink.OnApplied = _ => driver.SettleForShutdown(); //流到一半时进程要退出

        await driver.RunAsync(session, runner, Prompt());

        Assert.Equal(3, session.History.Count); //调用 + 取消结果 + 半截回复
        Assert.Equal(ChatRole.Tool, session.History[1].Role);
        Assert.Equal("半截回复", session.History[2].Text);
    }

    [Fact]
    public void SettleForShutdown_WhenIdle_DoesNothing()
    {
        FakeSink sink = new("不该被取走");
        TurnDriver driver = new(sink, new TurnUsageLedger());

        driver.SettleForShutdown();

        Assert.Empty(sink.StopNotes);
        Assert.Equal(0, sink.TakeStreamingTextCalls);
    }

    //================= 运行态与用量 =================

    [Fact]
    public async Task Registry_MarksTheSessionBusyDuringTheTurnAndIdleAfter()
    {
        ChatSession session = NewSession();
        FakeSink sink = new();
        bool busyDuringRun = false;
        sink.OnApplied = _ => busyDuringRun = SessionManager.Instance.Running.IsBusy(session.SessionId);

        StubRunner runner = new(Round(new TextContent("嗯")));
        TurnDriver driver = new(sink, new TurnUsageLedger());

        await driver.RunAsync(session, runner, Prompt());

        Assert.True(busyDuringRun);
        Assert.False(SessionManager.Instance.Running.IsBusy(session.SessionId));
    }

    [Fact]
    public async Task Usage_GoesToTheLedgerAndBackIntoTheSession()
    {
        //响应用量不随消息持久化,累计值记在本体上
        ChatSession session = NewSession();
        TurnUsageLedger ledger = new() { ContextLength = 128_000 };
        StubRunner runner = new(Round(Usage(1200, 340)));
        TurnDriver driver = new(new FakeSink(), ledger);

        await driver.RunAsync(session, runner, Prompt());

        Assert.Equal(1200, ledger.TurnInput);
        Assert.Equal(1200, ledger.LastInput);
        Assert.Equal(1200, session.TotalInputTokens);
        Assert.Equal(340, session.TotalOutputTokens);
        Assert.Equal(1200, session.LastInputTokens);
    }

    [Fact]
    public async Task Usage_ReportsObservedNotice()
    {
        List<TurnNotice> notices = new();
        TurnUsageLedger ledger = new() { ContextLength = 128_000 };
        StubRunner runner = new(Round(Usage(10, 5)));
        TurnDriver driver = new(new FakeSink(), ledger, notices.Add);

        await driver.RunAsync(NewSession(), runner, Prompt());

        Assert.Contains(ETurnNotice.UsageObserved, notices.Select(x => x.Kind));
    }

    //================= 交接文档 =================

    [Fact]
    public async Task Handoff_NotAttempted_WhenTurnProducedNoUsage()
    {
        //撞限流或被停止的那一轮拿不到任何响应,那一发交接请求多半也发不出去,
        //白白再走一遍五次退避
        List<TurnNotice> notices = new();
        ChatSession session = NewSession();
        for (int i = 0; i < 10; i++) session.History.Add(Prompt($"第 {i} 条"));

        StubRunner runner = new(Round(new TextContent("没有 usage")));
        TurnDriver driver = new(new FakeSink(), new TurnUsageLedger(), notices.Add);

        await driver.RunAsync(session, runner, Prompt());

        Assert.DoesNotContain(ETurnNotice.HandoffWritten, notices.Select(x => x.Kind));
        Assert.DoesNotContain(ETurnNotice.HandoffFailed, notices.Select(x => x.Kind));
        Assert.Equal(10, session.History.Count);
    }

    [Fact]
    public async Task Handoff_BelowWatermark_IsNotAttempted()
    {
        List<TurnNotice> notices = new();
        ChatSession session = NewSession();
        for (int i = 0; i < 10; i++) session.History.Add(Prompt($"第 {i} 条"));

        TurnUsageLedger ledger = new() { ContextLength = 128_000 };
        StubRunner runner = new(Round(Usage(500, 20))); //占用远在水位之下
        TurnDriver driver = new(new FakeSink(), ledger, notices.Add);

        await driver.RunAsync(session, runner, Prompt());

        Assert.DoesNotContain(ETurnNotice.HandoffWritten, notices.Select(x => x.Kind));
        Assert.Equal(10, session.History.Count);
    }

    [Fact]
    public async Task Handoff_Forced_WithTooFewMessages_ReportsNothingToCompact()
    {
        //上一份交接之后没攒下几条,再压一次只会把已经压过的东西再压一遍
        List<TurnNotice> notices = new();
        ChatSession session = NewSession();
        session.History.Add(Prompt("就一条"));

        TurnUsageLedger ledger = new() { ContextLength = 128_000 };
        TurnDriver driver = new(new FakeSink(), ledger, notices.Add);

        await driver.CompactAsync(session, new StubRunner());

        Assert.Equal([ETurnNotice.HandoffNothingToCompact], notices.Select(x => x.Kind));
        Assert.Single(session.History);
    }

    //================= 替身 =================

    private sealed class FakeSink : ITurnSink
    {
        private readonly Queue<string?> _streamingTexts;

        public FakeSink(params string?[] streamingTexts) => _streamingTexts = new Queue<string?>(streamingTexts);

        public List<AIContent> Applied { get; } = new();

        public List<string> StopNotes { get; } = new();

        public int CloseSegmentCalls { get; private set; }

        public int CloseNestedActivityCalls { get; private set; }

        public int TakeStreamingTextCalls { get; private set; }

        /// <summary>流到某一段时插进来做点别的（模拟用户点停止、进程退出）</summary>
        public Action<AIContent>? OnApplied { get; set; }

        public void Apply(AIContent content)
        {
            Applied.Add(content);
            OnApplied?.Invoke(content);
        }

        public void CloseSegment() => CloseSegmentCalls++;

        public void StopRunningToolCalls(string note) => StopNotes.Add(note);

        public void CloseNestedActivity() => CloseNestedActivityCalls++;

        public string? TakeStreamingText()
        {
            TakeStreamingTextCalls++;
            return _streamingTexts.Count > 0 ? _streamingTexts.Dequeue() : null;
        }
    }

    private sealed class StubRunner : ICharacterRunner
    {
        private readonly Queue<Func<IEnumerable<AIContent>>> _rounds;

        public StubRunner(params Func<IEnumerable<AIContent>>[] rounds) =>
            _rounds = new Queue<Func<IEnumerable<AIContent>>>(rounds);

        /// <summary>每一轮收到的输入消息</summary>
        public List<List<ChatMessage>> ReceivedRounds { get; } = new();

        public int SaveStateCalls { get; private set; }

        public bool HasSession => true;

        public ChatOptions? ChatOptions => null;

        public async IAsyncEnumerable<AIContent> RunAsync(IEnumerable<ChatMessage> messages,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ReceivedRounds.Add(messages.ToList());
            IEnumerable<AIContent> contents = _rounds.Count > 0 ? _rounds.Dequeue()() : [];
            foreach (AIContent content in contents)
            {
                yield return content;
                await Task.Yield();
            }
        }

        public Task AttachAsync(ChatSession session, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveStateAsync()
        {
            SaveStateCalls++;
            return Task.CompletedTask;
        }

        public IReadOnlyList<ChatMessage> GetHistory() => [];

        public Task<EAgentMode> GetModeAsync() => Task.FromResult(EAgentMode.Execute);

        public Task SetModeAsync(EAgentMode mode) => Task.CompletedTask;

        public Task<IReadOnlyList<TodoSnapshot>> GetTodosAsync() =>
            Task.FromResult<IReadOnlyList<TodoSnapshot>>([]);

        public Task<bool> TryInjectAsync(IEnumerable<ChatMessage> messages) => Task.FromResult(false);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
