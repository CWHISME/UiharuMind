using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 无头跑一轮的公共样板：借执行者（<see cref="ChatSession.AcquireRunnerAsync"/> 租约）→
/// <see cref="TurnDriver"/> 编排。与正常发送那条路同构——正常发送只是在这之上多了 UI 转录落点。
///
/// 群成员（<see cref="Chat.Group.HeadlessGroupMemberTurnRunner"/>）、唤醒轮
/// （<see cref="SessionWakeTurn"/>）、以及将来的「SendMessage 往单聊发一句并跑一轮」都从这里过；
/// 各调用方自己负责外围差异：运行登记（TryBeginRun / BeginRun）、群成员闸、审批 resolver 组装。
/// 从前这段样板在群成员与唤醒轮各写一份，加轮子的人再抄一遍，行为迟早走散。
/// </summary>
public static class SessionTurnRunner
{
    /// <summary>
    /// 跑一轮。调用前会话必须已经创建（本方法不做装配，与 <see cref="TurnDriver.RunAsync"/> 同口径）
    /// </summary>
    /// <param name="session">本轮的会话</param>
    /// <param name="input">用户消息；null 为唤醒轮（模型读已落进历史的结论）</param>
    /// <param name="resolver">审批回应口；null 表示不进入审批轮次（无头拒绝）</param>
    /// <param name="attended">有没有人能回应审批，与「有没有渲染落点」独立（唤醒轮的审批由窗口接）</param>
    /// <param name="onReplyFinishing">这一轮正文收尾时调（群成员用它把回复贴进群）；没有为 null</param>
    /// <param name="unansweredResultText">审批未决收口的结果正文；null 用默认「无人回应」口径</param>
    /// <param name="notify">生命周期通知（想收 Failed/Refused 等信号的调用方传）；不关心为 null</param>
    /// <param name="cancellationToken">外层的取消令牌，与本轮自己的取消源串起来</param>
    /// <returns>跑完了且没有被取消（失败/被拒/被取消都算 false）</returns>
    public static async Task<bool> RunHeadlessAsync(ChatSession session, ChatMessage? input,
        ApprovalResolver? resolver, bool attended, Func<Task>? onReplyFinishing = null,
        string? unansweredResultText = null, Action<TurnNotice>? notify = null,
        CancellationToken cancellationToken = default)
    {
        bool failed = false;
        // 整轮同一实例：Attach 与 Run 共用租约里的那一个，轮次中途的释放只延迟、不换实例
        using ChatSession.RunnerLease lease = await session.AcquireRunnerAsync(cancellationToken).ConfigureAwait(false);
        using TurnDriver driver = new(null, new TurnUsageLedger(),
            notice =>
            {
                if (notice.Kind is ETurnNotice.Failed or ETurnNotice.Refused) failed = true;
                notify?.Invoke(notice);
            }, unansweredResultText);

        lease.Runner.ReplyFinishing = onReplyFinishing == null ? null : _ => onReplyFinishing();
        try
        {
            await driver.RunAsync(session, lease.Runner, input, resolver, cancellationToken, attended)
                .ConfigureAwait(false);
        }
        finally
        {
            lease.Runner.ReplyFinishing = null;
        }

        return !failed && !cancellationToken.IsCancellationRequested;
    }
}
