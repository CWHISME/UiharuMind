using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Tools.BackgroundTasks;
using UiharuMind.Core.AI.Execution.ToolCall;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>
/// <c>SendMessage</c> 的单聊分支：往一个<b>普通单聊会话</b>里说一句。
///
/// 与发群同一语义——就是<b>用户的话</b>：直接落进那个会话的历史（跟用户在那边打字一样，
/// 界面照常收到追加通知）。随后起一个<b>等待任务</b>（进程内后台任务，见
/// <see cref="BackgroundTaskRegistry.StartInline"/>）：任务体跑那一轮，跑完把最后一句
/// 作为结果送回<b>发送方</b>（走 <see cref="SessionReportSink"/> → 唤醒轮，同子代理报告与后台任务的送法）。
///
/// 不额外加来源标记——接收方看到的就是用户的话，它不知道这句话是主代理替用户发的。
/// 接收方正在跑（用户可能正在那边聊）就如实报错，等这一轮结束再发——
/// 那不是该插进别人正在回的轮子里的话，硬插会和用户在那边正常打字撞车。
/// </summary>
internal static class SessionMessenger
{
    /// <summary>审批无人应的等待上限：与群成员同口径，到期按拒绝收口，这一轮不至于一直挂着</summary>
    private static readonly TimeSpan ApprovalTimeout = TimeSpan.FromMinutes(10);

    /// <summary>嵌套审批连续被拒造成的追加轮次上限：与群成员同口径</summary>
    private const int MaxDeniedApprovalRounds = 3;

    /// <summary>无头跑一轮的可注入口（测试换假轮子；运行态就是 <see cref="SessionTurnRunner.RunHeadlessAsync"/>）</summary>
    internal static Func<ChatSession, ChatMessage, ApprovalResolver?, bool, CancellationToken, Task<bool>> TurnRunner =
        (session, input, resolver, attended, token) =>
            SessionTurnRunner.RunHeadlessAsync(session, input, resolver, attended, cancellationToken: token);

    /// <summary>
    /// 起等待任务的默认实现：经 <see cref="BackgroundTaskRegistry.StartInline"/> 真起。
    /// 任务的<b>归属会话是发送方</b>——<see cref="SessionReportSink"/> 按归属送信，
    /// 填成接收方的话报告会落到被发的那一边，发送方永远等不到
    /// </summary>
    internal static readonly Action<string, ChatSession, int, ChatMessage> DefaultStartWaiter =
        (senderSessionId, session, start, input) =>
            BackgroundTaskRegistry.StartInline(senderSessionId, $"等《{session.Title}》回这一轮",
                WaitTaskLimits.MaxRuntime, token => RunAndCollectAsync(session, start, input, token),
                SessionReportSink.Instance);

    /// <summary>
    /// 起等待任务的入口。默认 <see cref="DefaultStartWaiter"/>；测试换假件记录调用，
    /// 避免在测试里真起后台任务、真投递、真唤醒
    /// </summary>
    internal static Action<string, ChatSession, int, ChatMessage> StartWaiter = DefaultStartWaiter;

    /// <summary>
    /// 按标题或短号找普通单聊会话：不是子会话、不是群壳、不是群成员（成员与化身有自己的群通道）。
    /// 一个即唯一命中，零个即没有，多个即撞名/撞号
    /// </summary>
    /// <param name="target">模型填的收件人（单聊标题或会话短号）</param>
    /// <returns>命中的会话元数据</returns>
    internal static IReadOnlyList<ChatSessionMeta> Match(string target)
    {
        string wanted = target.Trim().Trim('《', '》', '「', '」');
        if (wanted.Length == 0) return [];

        List<ChatSessionMeta> all = SessionManager.Instance.FindSessions(x =>
                !x.IsSubSession && !x.IsGroup && !x.IsGroupMember)
            .OrderByDescending(x => x.UpdatedAt)
            .ToList();
        if (all.Count == 0) return [];

        List<ChatSessionMeta> byId = all
            .Where(x => x.SessionId.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return byId.Count > 0
            ? byId
            : all.Where(x => string.Equals(x.Title, wanted, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>最近几个普通单聊（报错时列给模型挑：它不知道有哪些会话，光有「填标题」它编不出来）</summary>
    /// <param name="limit">列几个</param>
    /// <returns>会话元数据，按最后更新倒序</returns>
    internal static IReadOnlyList<ChatSessionMeta> RecentSingles(int limit = 5) =>
        SessionManager.Instance.GetSessions()
            .Where(x => !x.IsSubSession && !x.IsGroup && !x.IsGroupMember)
            .Take(limit)
            .ToList();

    /// <summary>
    /// 以用户的名义往单聊里说一句，并起等待任务让它回一轮、把最后一句送回发送方
    /// </summary>
    /// <param name="target">目标会话元数据</param>
    /// <param name="text">要说的话</param>
    /// <param name="senderSessionId">发送方会话标识：报告送回这里</param>
    /// <returns>给模型的回执</returns>
    internal static string Post(ChatSessionMeta target, string text, string senderSessionId)
    {
        if (SessionManager.Instance.Load(target.SessionId) is not { } body)
        {
            return $"Error: 会话《{target.Title}》打不开，这句没发出去。";
        }

        if (SessionManager.Instance.Running.IsBusy(body.SessionId))
        {
            return $"Error: 《{body.Title}》正在跑（可能你或用户正在那边聊），等这一轮结束再发。";
        }

        // 落一条「用户的话」并广播追加通知：那边开着的窗口会像用户打字一样实时看到
        ChatMessage message = body.CreateMessage(ChatRole.User, text);
        int start = body.History.Count;
        body.History.Add(message);
        body.SaveAppended(start);

        // 起等待任务：任务体跑那一轮，跑完把最后一句送回发送方（醒着插进那一轮、闲着落盘再唤醒）
        StartWaiter(senderSessionId, body, start, message);

        return $"已以用户的名义发到《{body.Title}》，它回完这一轮会自动送一封回信（会唤醒你，不用轮询）。";
    }

    /// <summary>
    /// 等待任务的任务体：跑那一轮（审批有人接：目标会话的窗口开着就弹卡等用户点，没人应按超时拒绝收口，
    /// 与群成员同口径），然后从这一轮追加的段里取最后一句有字的助手正文作为结果
    /// </summary>
    /// <param name="session">接收方会话（Post 时已加载的那个实例）</param>
    /// <param name="start">这一轮的起点：Post 落完那条 user 消息后的历史长度</param>
    /// <param name="input">那条 user 消息</param>
    /// <param name="cancellationToken">任务级取消：用户叫停等待任务或到时间上限时<b>只结束等待</b>，
    /// 那一轮照常跑完（等待只是「听」，没权中止别人那边正在回的轮子）</param>
    /// <returns>送还发送方的报告正文</returns>
    internal static async Task<string> RunAndCollectAsync(ChatSession session, int start, ChatMessage input,
        CancellationToken cancellationToken)
    {
        // 轮次不挂任务级取消：叫停/超时只结束等待本身。异常不在这里兜——失败由 TurnRunner 以 false 返回
        ApprovalResolver? resolver = NestedApprovalResolver.Create(true, session.SessionId,
            SessionApprovalRegistry.Instance, ApprovalTimeout, MaxDeniedApprovalRounds,
            CancellationToken.None, onWaiting: null);
        Task<bool> turn = TurnRunner(session, input, resolver, true, CancellationToken.None);
        turn.LogOnFault($"run a single-chat turn for '{session.Title}'");

        // 取消时这里抛 OCE，任务当场收口；turn 自己在后台跑完落盘
        bool completed = await turn.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!completed)
        {
            return $"《{session.Title}》那一轮没跑完（失败或被停），没有可交回的结论。";
        }

        string last = LastAssistantSince(session, start);
        if (last.Length == 0)
        {
            return $"《{session.Title}》那边回完了这一轮，但没有留下可读的回复。";
        }

        return $"《{session.Title}》那边回完了这一轮，最后一句是：\n\n{BackgroundTaskReport.QuoteForReport(last)}";
    }

    /// <summary>
    /// 从 <paramref name="start"/> 起新增的段里找最后一条有字的助手正文。
    /// 范围限定在那一轮是诚实的：接收方回完可能又跟用户聊起来，捞全文会把后来的话混进来；
    /// 中途用户插话（Post 与轮末之间用户在那边打字）仍可能混入——这是已知边界
    /// </summary>
    private static string LastAssistantSince(ChatSession session, int start)
    {
        try
        {
            IList<ChatMessage> history = session.History;
            for (int i = history.Count - 1; i >= start; i--)
            {
                ChatMessage message = history[i];
                if (message.Role != ChatRole.Assistant) continue;
                string text = message.Text?.Trim() ?? string.Empty;
                if (text.Length > 0) return text;
            }
        }
        catch (Exception e)
        {
            // 历史正被压缩/卸载等并发读写：读不出就按没产出收口，异常只落日志
            Log.Warning($"Read last assistant of '{session.SessionId}' failed: {e.Message}");
        }

        return string.Empty;
    }
}
