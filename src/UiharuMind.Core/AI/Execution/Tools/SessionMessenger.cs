using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.ToolCall;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>
/// <c>SendMessage</c> 的单聊分支：往一个<b>普通单聊会话</b>里说一句。
///
/// 与发群同一语义——就是<b>用户的话</b>：直接落进那个会话的历史（跟用户在那边打字一样，
/// 界面照常收到追加通知），然后让它回一轮。不做任何特殊处理：不加来源标记、不把回复送还发送方、
/// 不登记待交回。接收方正在跑（用户可能正在那边聊）就如实报错，等这一轮结束再发——
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
    /// 以用户的名义往单聊里说一句，并让它回一轮
    /// </summary>
    /// <param name="target">目标会话元数据</param>
    /// <param name="text">要说的话</param>
    /// <returns>给模型的回执</returns>
    internal static string Post(ChatSessionMeta target, string text)
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
        int from = body.History.Count;
        body.History.Add(message);
        body.SaveAppended(from);

        // 后台跑一轮让它回应；异常只记日志（不该毁掉主代理这一轮）
        ChatSession running = body;
        RunTurnDetachedAsync(running, message).LogOnFault($"run a single-chat turn for '{body.Title}'");
        return $"已以用户的名义发到《{body.Title}》，它会在那边回一句。";
    }

    private static async Task RunTurnDetachedAsync(ChatSession session, ChatMessage input)
    {
        // 审批有人接：目标会话的窗口开着就弹卡等用户点，没人应按超时拒绝收口（与群成员同口径）
        ApprovalResolver? resolver = NestedApprovalResolver.Create(true, session.SessionId,
            SessionApprovalRegistry.Instance, ApprovalTimeout, MaxDeniedApprovalRounds,
            CancellationToken.None, onWaiting: null);
        await TurnRunner(session, input, resolver, true, CancellationToken.None).ConfigureAwait(false);
    }
}