/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 驱动一轮对话：发送 → 流式装配 → 审批回环 → 取消收尾 → 交接文档 → 存档。
///
/// 它把<see cref="ICharacterRunner"/>（装配好的可运行体）跑起来，并负责这一轮的历史自洽性。
/// 界面与无头执行（定时任务）跑的是同一份编排，差异只有两处：渲染落点
/// （<see cref="ITurnSink"/>，无头传 null）与审批回应的取得方式（<see cref="ApprovalResolver"/>）。
///
/// 一个实例服务一个调用方、跨轮存活（token 账本是跨轮累计的），会话与执行者逐轮传入。
/// </summary>
public sealed class TurnDriver : IDisposable
{
    /// <summary>少于这个数压了也没意义</summary>
    private const int MinMessagesToCompact = 4;

    /// <summary>
    /// 还活着的实例。退出收尾要给每个仍在跑的轮次补上取消结果，而调用方可能是页面、
    /// 可能是快速对话窗口、也可能是定时任务——没有一个统一的宿主可以遍历，索引记在这里。
    /// </summary>
    private static readonly List<TurnDriver> _liveDrivers = new();

    private static bool _usageKeysLogged; //附加计数的键名各家不同,只需认一次

    private readonly ITurnSink? _sink;
    private readonly TurnUsageLedger _usage;
    private readonly Action<TurnNotice>? _notify;

    private CancellationTokenSource? _runCancellation;
    private ChatSession? _activeSession; //本轮的会话,退出收尾要靠它
    private bool _isRunning;
    private bool _isCompacting;

    /// <summary>本轮是否正在跑</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (_isRunning == value) return;
            _isRunning = value;
            StateChanged?.Invoke();
        }
    }

    /// <summary>是否正在整理交接文档（界面据此提示，避免看起来像卡住）</summary>
    public bool IsCompacting
    {
        get => _isCompacting;
        private set
        {
            if (_isCompacting == value) return;
            _isCompacting = value;
            StateChanged?.Invoke();
        }
    }

    /// <summary><see cref="IsRunning"/> 或 <see cref="IsCompacting"/> 变化</summary>
    public event Action? StateChanged;

    /// <param name="sink">渲染落点；无头执行传 null</param>
    /// <param name="usage">token 账本，跨轮累计。交接的水位判定读它，调用方也用它显示占用</param>
    /// <param name="notify">生命周期通知；不关心传 null</param>
    public TurnDriver(ITurnSink? sink, TurnUsageLedger usage, Action<TurnNotice>? notify = null)
    {
        _sink = sink;
        _usage = usage;
        _notify = notify;
        lock (_liveDrivers) _liveDrivers.Add(this);
    }

    /// <summary>
    /// 跑一轮。
    ///
    /// 调用前会话必须已经创建并挂接好执行者——本方法不做会话装配，
    /// 因为「新建会话」牵扯标题、列表、面板一整套界面动作，那属于调用方。
    /// </summary>
    /// <param name="session">本轮的会话</param>
    /// <param name="runner">该会话的执行者（必须是已 <c>AttachAsync</c> 到 <paramref name="session"/> 的那一个）</param>
    /// <param name="userMessage">用户消息（已装配好附件与技能正文）</param>
    /// <param name="resolver">审批回应的取得方式；传 null 表示不进入审批轮次</param>
    /// <param name="thinkingMode">本轮思考力度</param>
    public async Task RunAsync(ChatSession session, ICharacterRunner runner, ChatMessage userMessage,
        ApprovalResolver? resolver = null, EThinkingMode thinkingMode = EThinkingMode.Default)
    {
        IsRunning = true;
        _activeSession = session;
        // 思考力度随本次异步流下发到 HTTP 层(SDK 无逐请求参数通道)
        LlmRequestContext.ThinkingMode = thinkingMode;
        _usage.BeginTurn();
        _notify?.Invoke(new TurnNotice(ETurnNotice.Started)); //本轮实际使用的模型此刻可解析
        _runCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _runCancellation.Token;
        try
        {
            List<ChatMessage>? nextMessages = new() { userMessage };

            // 登记运行态,直到本轮彻底结束:切走这个会话之后它仍在跑,界面靠这个标记
            // 在列表与导航栏上把它显示出来,删除与清空历史也据此拦下
            using IDisposable runScope = SessionManager.Instance.Running.BeginRun(session.SessionId);

            // 开跑就算一次活动:刷新 UpdatedAt,让会话此刻就浮到列表顶部。
            // 否则要等一轮跑完(历史落盘才更新时间戳),界面上是回复结束后突然跳位
            session.SaveMeta();
            // 给本轮的请求消息盖上时间:框架交给持久化的是丢了时间戳的副本,
            // 而落盘发生在一轮跑完之后(见 ChatSession.TurnStartedAt)
            session.TurnStartedAt = DateTimeOffset.Now;

            while (nextMessages is { Count: > 0 })
            {
                List<ToolApprovalRequestContent> roundRequests = new();
                try
                {
                    await foreach (AIContent content in runner.RunAsync(nextMessages, cancellationToken))
                    {
                        if (content is ToolApprovalRequestContent request) roundRequests.Add(request);
                        if (content is UsageContent usage) RecordUsage(session, usage.Details);
                        _sink?.Apply(content);
                    }
                }
                catch (OperationCanceledException)
                {
                    SettleCancelledTurn(session);
                    break;
                }

                _sink?.CloseSegment();
                _notify?.Invoke(new TurnNotice(ETurnNotice.RoundCompleted));

                if (roundRequests.Count == 0 || resolver == null) break;

                // 审批往返:等待回应,回应作为下一轮输入。
                // 这段等待要单独登记:会话切走后审批卡片跟着看不见了,那一轮就静静挂在这里,
                // 只有把它与「在跑」区分开,列表与导航栏才能提示用户回来处理
                using (SessionManager.Instance.Running.BeginApprovalWait(session.SessionId))
                {
                    nextMessages = (await resolver(roundRequests)).ToList();
                }

                if (cancellationToken.IsCancellationRequested) break;
            }

            await runner.SaveStateAsync();
            _notify?.Invoke(new TurnNotice(ETurnNotice.Persisted));
        }
        catch (Exception e)
        {
            Log.Error($"Agent turn failed: {e}");
            _notify?.Invoke(new TurnNotice(ETurnNotice.Failed, e.Message));
        }
        finally
        {
            _sink?.CloseSegment();
            // 中途停止(或出错)时那条工具结果永远不会来,卡片会一直转圈。放在收尾里而不是取消分支里:
            // 出错路径同样收不到结果,而正常结束时本就没有还在跑的调用,这里是空操作。
            // 不做本地化:补写进历史的是同一句英文,重开会话时卡片显示的就是它,两边措辞得一致
            _sink?.StopRunningToolCalls(ToolCallCancellation.ResultText);
            _sink?.CloseNestedActivity();
            IsRunning = false;
            _runCancellation = null;
            _activeSession = null;
            _notify?.Invoke(new TurnNotice(ETurnNotice.Ended));
        }

        // 压缩放在轮次之间,不放在请求路径上:写交接文档本身要发一次请求,
        // 塞进本轮会让用户多等一次完整往返
        await WriteHandoffAsync(session, runner, force: false);
    }

    /// <summary>
    /// 手动触发交接（<c>/compact</c>）：任务的自然边界由人判断，在边界上压缩质量高得多
    /// </summary>
    /// <param name="session">会话</param>
    /// <param name="runner">该会话的执行者</param>
    public Task CompactAsync(ChatSession session, ICharacterRunner runner) =>
        WriteHandoffAsync(session, runner, force: true);

    /// <summary>
    /// 请求停止本轮。只取消，补写取消结果由运行循环自己在取消分支里做；
    /// 进程即将消失的场合用 <see cref="SettleForShutdown"/>。
    /// </summary>
    public void Cancel()
    {
        _runCancellation?.Cancel();
    }

    /// <summary>
    /// 退出收尾：取消本实例正在跑的那一轮并<b>当场</b>补上取消结果。
    ///
    /// 不能只取消了事——进程马上就没了，运行循环等不到观察取消的那一刻，
    /// 历史里会留下没有配对结果的 tool_call，下次打开这个会话直接 400。
    /// 重复调用安全：补写只针对没配对的调用，正文只取还在流的那一段，第二次两者都为空。
    /// </summary>
    public void SettleForShutdown()
    {
        if (!IsRunning) return;
        _runCancellation?.Cancel();
        if (_activeSession is { } session) SettleCancelledTurn(session);
    }

    /// <summary>
    /// 给所有还在跑的轮次补上取消结果（退出时调用）
    /// </summary>
    public static void SettleAllForShutdown()
    {
        TurnDriver[] drivers;
        lock (_liveDrivers) drivers = _liveDrivers.ToArray();
        foreach (TurnDriver driver in drivers)
        {
            try
            {
                driver.SettleForShutdown();
            }
            catch (Exception e)
            {
                //退出路径上一个会话收尾失败不能拖累其余会话
                Log.Warning($"Settle running turn on shutdown failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// 弃用本实例：注销索引、取消正在跑的那一轮（不在这里补写，运行循环还活着）
    /// </summary>
    public void Dispose()
    {
        lock (_liveDrivers) _liveDrivers.Remove(this);
        _runCancellation?.Cancel();
    }

    /// <summary>
    /// 账本记一次用量，并把增量写回会话本体——响应用量不随消息持久化，
    /// 累计值记在本体上，随轮末的历史保存一并落盘。
    /// </summary>
    /// <param name="session">当前会话</param>
    /// <param name="details">服务端报的这一次用量</param>
    private void RecordUsage(ChatSession session, UsageDetails details)
    {
        // 前缀缓存到底有没有生效,只能看服务端报的这个数——推理不出来。
        // 键名先打出来认一次:各家不同,MEAI 映射后还会再改一次名
        if (!_usageKeysLogged && details.AdditionalCounts is { Count: > 0 } counts)
        {
            _usageKeysLogged = true;
            Log.Debug($"Usage additional counts: {string.Join(", ", counts.Select(x => $"{x.Key}={x.Value}"))}");
        }

        (long input, long output) = _usage.Add(details);
        session.TotalInputTokens += input;
        session.TotalOutputTokens += output;
        session.LastInputTokens = _usage.LastInput; //占用随本体持久化,切回会话时不必等下一次响应
        _notify?.Invoke(new TurnNotice(ETurnNotice.UsageObserved));
    }

    /// <summary>
    /// 收拾被取消的一轮，让历史回到自洽状态。两件事：
    /// <list type="number">
    /// <item>给没等到结果的工具调用补上取消结果——否则历史里留着孤儿 tool_call，
    /// 严格的服务端（OpenAI、Anthropic）会直接 400，这个会话从此发不出话。</item>
    /// <item>把已经吐出来的半截回复写进历史——框架在失败路径上不落任何响应消息
    /// （<see cref="SessionChatHistoryProvider"/> 补回了请求消息，但响应侧它拿不到），
    /// 不补的话界面上留着半截回复、重开会话却没了，模型也不知道自己说过什么。</item>
    /// </list>
    ///
    /// <b>正文只取正在流的那一段</b>：本轮更早的段落已经由框架逐次服务调用各自落过盘，
    /// 再写一遍就会在会话里多出一句一模一样的话（切换会话回来才看得见）。
    /// </summary>
    /// <param name="session">当前会话</param>
    private void SettleCancelledTurn(ChatSession session)
    {
        // 先补工具结果再落正文:结果必须紧跟在它那条调用之后,顺序反了历史就对不上
        ToolCallCancellation.CloseUnansweredAtTail(session);

        if (_sink?.TakeStreamingText() is not { } text) return;

        int before = session.History.Count;
        session.History.Add(session.CreateMessage(ChatRole.Assistant, text));
        session.SaveAppended(before);
        //落库后由调用方把界面条目与消息配对(Persisted 通知),气泡因此照常拿到编辑/重试/分叉
    }

    /// <summary>
    /// 到水位就让当前模型写一份交接文档，之后的历史供给从它开始。
    /// 失败不作声张——框架的截断仍挂着当兜底，最坏结果是回到「悄悄丢最旧的消息」。
    /// </summary>
    /// <param name="session">会话</param>
    /// <param name="runner">该会话的执行者</param>
    /// <param name="force">手动触发（<c>/compact</c>），跳过水位判定</param>
    private async Task WriteHandoffAsync(ChatSession session, ICharacterRunner runner, bool force)
    {
        // 本轮没拿到任何响应(撞限流、被停止)时不自动压缩:那一发交接请求多半也发不出去,
        // 白白再走一遍五次退避
        if (!force && _usage.TurnInput == 0) return;

        // 上限只在没人填过时现读:界面侧本来就随模型变化刷新账本上的这个数,
        // 无头执行没有界面,不补的话水位永远算不出来、交接也就永不触发
        if (_usage.ContextLength == 0)
        {
            _usage.ContextLength = session.ChatModelRunningData?.ContextLength ?? 0;
        }

        if (!force && !HistoryHandoff.ShouldWrite(_usage.LastInput, _usage.ContextLength)) return;

        int start = HistoryHandoff.SupplyStartIndex(session.History);
        // 上一份交接之后没攒下几条,再压一次只会把已经压过的东西再压一遍
        if (session.History.Count - start < MinMessagesToCompact)
        {
            if (force) _notify?.Invoke(new TurnNotice(ETurnNotice.HandoffNothingToCompact));
            return;
        }

        // ChatModelRunningData 取不到会话自己那份时已经回落到当前运行模型,这里不必再兜一层
        IChatClient? client = session.ChatModelRunningData?.ChatClient;
        if (client == null) return;

        IsCompacting = true;
        try
        {
            List<ChatMessage> supplied = session.History.Skip(start).ToList();
            // 选项取本会话装配好的那一份(系统提示词 + 工具定义 + 采样参数),与常规轮次逐字一致
            string? note = await HistoryHandoff.WriteAsync(client, supplied, runner.ChatOptions,
                _usage.ContextLength, CancellationToken.None);
            if (note == null)
            {
                _notify?.Invoke(new TurnNotice(ETurnNotice.HandoffFailed));
                return;
            }

            ChatMessage message = HistoryHandoff.CreateNote(note);
            int before = session.History.Count;
            session.History.Add(message);
            session.SaveAppended(before);
            //正文从消息本体取而不是直接用 note:去标题这一步与写进历史的那份逐字对应
            _notify?.Invoke(new TurnNotice(ETurnNotice.HandoffWritten, HistoryHandoff.NoteBody(message.Text)));
            _notify?.Invoke(new TurnNotice(ETurnNotice.ScrollToEnd));
        }
        finally
        {
            IsCompacting = false;
        }
    }
}
