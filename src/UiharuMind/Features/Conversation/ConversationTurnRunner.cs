/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.History;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 跑一轮的宿主机位：运行器只吃这几个窄依赖，不反向持有视图模型
/// （与 <see cref="IConversationReconcileHost"/> 同一做法）。
/// </summary>
public interface IConversationTurnHost
{
    /// <summary>当前会话标识；首轮发送前尚无会话为 null</summary>
    string? CurrentSessionId { get; }

    /// <summary>当前会话本体；尚无会话为 null</summary>
    ChatSession? CurrentSession { get; }

    /// <summary>取得当前会话并挂好执行者；还没有就新建</summary>
    /// <param name="titleSeed">新建会话时取标题的原文</param>
    /// <param name="cancellationToken">装配阶段的取消</param>
    /// <returns>会话本体</returns>
    Task<ChatSession> EnsureSessionAsync(string titleSeed, CancellationToken cancellationToken);

    /// <summary>会话已就位（附件盘里粘贴图落的盘此刻才有归属）</summary>
    void OnSessionEnsured();

    /// <summary>装配态变了：停止按钮与发送按钮文案跟着它</summary>
    void NotifyPreparingChanged();

    /// <summary>把这一轮起不来的原因落到会话流里</summary>
    /// <param name="message">错误文案</param>
    void ShowError(string message);
}

/// <summary>
/// 一轮对话的入口：先把会话装配好、过闸，再交给 <see cref="TurnDriver"/>。
///
/// 装配阶段单独持一个取消源——那时驱动还没接手，而它耗时（要建会话、装配 agent），
/// 用户在这期间按停止必须停得下来。
/// </summary>
public sealed class ConversationTurnRunner
{
    private readonly IConversationTurnHost _host;
    private readonly TurnDriver _driver;
    private readonly ConversationTranscript _transcript;
    private CancellationTokenSource? _prepareCancellation; //会话装配阶段的取消源,此后由 TurnDriver 接手

    /// <summary>正在装配会话（此时驱动还没开始跑，但界面必须已经显示停止按钮）</summary>
    public bool IsPreparing { get; private set; }

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="host">宿主</param>
    /// <param name="driver">一轮对话的编排</param>
    /// <param name="transcript">实时流装配器（审批卡在它那里）</param>
    public ConversationTurnRunner(IConversationTurnHost host, TurnDriver driver, ConversationTranscript transcript)
    {
        _host = host;
        _driver = driver;
        _transcript = transcript;
    }

    /// <summary>
    /// 跑一轮
    /// </summary>
    /// <param name="userMessage">用户消息;为 null 是无输入轮(助手消息重试),模型基于既有历史续写</param>
    /// <param name="titleSeed">新建会话时用来取标题的原文</param>
    public async Task RunAsync(ChatMessage? userMessage, string titleSeed)
    {
        IsPreparing = true;
        _host.NotifyPreparingChanged();
        _prepareCancellation = new CancellationTokenSource();
        ChatSession? session = null; //提到 try 外:装配被停时还要靠它把 userMessage 补回历史
        try
        {
            // 装配阶段也登记成「在跑」:重建 agent 要拉 MCP 工具、可能好几秒,
            // 这期间不能让删除/清空去动它的文件,而那一轮随后照样会往里写。
            // 新会话此刻还没有标识,BeginRun(null) 按设计是空操作
            // 这一份**一直持有到本轮结束**,不在装配结束时放掉。从前是装配一段、运行一段两个作用域,
            // 注释写着「两段之间没有空窗」——单线程看确实没有,但登记处的锁在两段之间放开了,
            // 别的线程(后台委派回来时起的唤醒轮)正好能在这里挤进来抢到会话,两轮就重叠了。
            // 登记是引用计数的,与 TurnDriver 自己那一次叠加无害
            using IDisposable running = SessionManager.Instance.Running.BeginRun(_host.CurrentSessionId);
            // 群成员会话的私聊与群轮投递共用同一把闸:群轮占着就叫停它、等它停稳(ADR 0063),两轮永不重叠。
            // 要在装配之前过:群轮整轮持着执行者的锁,先装配就会一直等在那把锁上,根本走不到叫停这一步
            using IDisposable memberGate = await GroupMemberTurnGate
                .EnterAsync(_host.CurrentSessionId, _prepareCancellation.Token);
            session = await _host.EnsureSessionAsync(titleSeed, _prepareCancellation.Token);
            _host.OnSessionEnsured();

            // 子会话与后台轮共用同一把串行闸（后台那轮整轮持有：跑+交回）：用户在子会话窗口
            // 直发必须等后台那一轮结束，否则两轮在 runner 释放/重建上重叠——后台轮 finally 释放
            // 旧实例，此刻正在 Attach/Run 的这一轮会拿到没挂接的新 runner（实机「尚未挂接会话」）。
            // 主会话不过闸（它的后台轮另走 TryBeginRun 抢占）。
            using IDisposable? turnGate = await BackgroundSubAgentDispatcher
                .EnterSubSessionTurnGateAsync(session.SessionId).ConfigureAwait(false);
            try
            {
                await _driver.RunAsync(session, session.Runner, userMessage, ResolveApprovalsAsync);
            }
            finally
            {
                await WithdrawPrivateInjectionsAsync(session);
            }
        }
        catch (OperationCanceledException)
        {
            // 装配阶段就被停掉:TurnDriver 还没接手,userMessage 从未交给框架,历史里自然也没有它。
            // 发送方(发送/重试)已经把「这条消息必须在历史里」当成 RunAsync 的责任,责任悬空就丢消息
            // (重试最典型:Retry 先删后跑,这里不补回,切走/重开会话那条输入就没了)
            RestoreUserMessageOnAbort(session ?? _host.CurrentSession, userMessage);
        }
        catch (Exception e)
        {
            Log.Error($"Ensure session failed: {e}");
            _host.ShowError(e.Message);
        }
        finally
        {
            IsPreparing = false;
            _prepareCancellation = null;
            _host.NotifyPreparingChanged();
        }
    }

    // 群成员会话：这一轮没被消费的私聊插话在放闸前撤掉。放闸可能当场接回群轮，用的是同一个执行者，
    // 留着会被群轮取走、回复进群。界面那份待发提示另由轮次结束时还回输入框
    private static async Task WithdrawPrivateInjectionsAsync(ChatSession session)
    {
        if (!session.IsGroupMember) return;

        List<ChatMessage> leftover = session.Runner.PendingInjections
            .Where(ChatMessageAnnotations.IsGroupPrivate)
            .ToList();
        if (leftover.Count == 0) return;

        try
        {
            await session.Runner.CancelInjectionsAsync(leftover).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Warning($"Withdraw private interjections failed: {e.Message}");
        }
    }

    /// <summary>
    /// 审批回应：等用户对本轮每个请求做出决定，回应即下一轮的输入。
    ///
    /// <b>按请求对象相认</b>，不是把转录器攒着的整批抽干：同一个会话上可能同时有两轮在跑
    /// （用户那一轮与后台委派回来时起的<b>唤醒轮</b>共用这一个转录器），抽干会领走别人那一轮的卡片，
    /// 让那一轮的工具调用永远没有结果地留在历史里。
    /// </summary>
    /// <param name="requests">本轮新增的审批请求</param>
    /// <returns>回应消息</returns>
    public async Task<IReadOnlyList<ChatMessage>> ResolveApprovalsAsync(
        IReadOnlyList<ToolApprovalRequestContent> requests)
    {
        IReadOnlyList<ApprovalRequestItem> turnApprovals = _transcript.TakeRoundApprovals(requests);
        List<ChatMessage> responses = new(turnApprovals.Count);
        foreach (ApprovalRequestItem approval in turnApprovals)
        {
            responses.Add(await approval.Response);
        }

        _transcript.ResolveApprovals(turnApprovals);
        return responses;
    }

    /// <summary>停下自己这一轮：还卡在装配阶段的也停得下来</summary>
    public void Cancel()
    {
        CancelPreparing();
        _driver.Cancel();
    }

    /// <summary>只停装配阶段（弃用视图时用：驱动另由它自己的 Dispose 收尾）</summary>
    public void CancelPreparing()
    {
        _prepareCancellation?.Cancel();
    }

    /// <summary>
    /// 装配阶段被取消时把 userMessage 补回历史,避免「发送/重试后立刻停止」丢消息。
    /// 正常轮次的取消由 <see cref="TurnDriver"/> 的 <c>SettleInterruptedTurn</c> 收尾,
    /// 这里只兜它接手之前的那段空窗——那时 userMessage 还没交给框架,没有人会写它。
    /// 无输入轮(助手消息重试)没有用户消息要补,直接返回。
    /// </summary>
    /// <param name="session">会话;新建会话装配半路取消时为 null(此时无处可写)</param>
    /// <param name="userMessage">本轮输入;无输入轮为 null</param>
    internal static void RestoreUserMessageOnAbort(ChatSession? session, ChatMessage? userMessage)
    {
        if (session == null)
        {
            Log.Warning("Turn aborted during session assembly; user message was not persisted.");
            return;
        }

        // 无输入轮(助手消息重试):没有用户消息被删,也就没有要补回的东西
        if (userMessage == null) return;

        // 与 TurnDriver 同口径:重试的原消息带着框架就地盖的 _attribution,
        // 不摘掉持久化会把它当注入消息滤掉(见 RunAsync 开头的 ClearAttribution)
        ChatMessageAnnotations.ClearAttribution(userMessage);
        // 取消前若恰好已写回(罕见)就别重复追加:框架落的是同一个实例,按引用判重即可
        if (session.History.Any(x => ReferenceEquals(x, userMessage))) return;

        int before = session.History.Count;
        session.History.Add(userMessage);
        session.SaveAppended(before);
    }
}
