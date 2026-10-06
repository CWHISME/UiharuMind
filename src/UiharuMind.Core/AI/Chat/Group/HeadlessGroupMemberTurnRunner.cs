using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.ToolCall;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 成员一轮的默认跑法：与定时任务同一条无头编排（<see cref="TurnDriver"/>，没有渲染落点），
/// 运行态登记、取消收尾、交接文档一并到手。
///
/// 审批登记到 <see cref="SessionApprovalRegistry"/> 等人点选（ADR 0046 修订）：群的待审批条与他自己的
/// 会话窗口都能认领那张卡。只有他这一轮停下来等，其余成员照常说话；没人应就按拒绝收口，
/// 连着几轮一条都没批准就收掉这一轮，免得模型换着花样一直要。
/// </summary>
public sealed class HeadlessGroupMemberTurnRunner : IGroupMemberTurnRunner
{
    private const int MaxDeniedApprovalRounds = 3; //连着几轮一条都没批准就收口

    /// <summary>审批无人应时的等待上限：到期按拒绝收口，这一波群聊不至于一直挂着</summary>
    public static readonly TimeSpan ApprovalTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 有成员开始等审批（参数为成员会话）。界面据此提示用户——人不在群视图里就看不到那条待审批条。
    /// 静态口，与后台子代理的提示同形（ADR 0025）。⚠️ 来自后台线程
    /// </summary>
    public static Action<ChatSession>? ApprovalWaitingNotifier { get; set; }

    /// <summary>
    /// 离席期间的审批通道（ADR 0055）：返回非空就由化身接，不等用户。每批审批现问一次——离席可能在一轮中途开始或结束。
    /// 静态口，由 <c>GroupAwayController.Instance</c> 接上
    /// </summary>
    public static Func<ChatSession, CancellationToken, ApprovalResolver?>? AwayApprovals { get; set; }

    /// <inheritdoc />
    public async Task<bool> RunAsync(ChatSession member, ChatMessage input, Func<Task>? onReplyFinishing,
        CancellationToken cancellationToken)
    {
        // 整轮同一实例：Attach 与 Run 共用租约里的那一个，用户私聊抢占/停止并发时也不换实例
        using ChatSession.RunnerLease lease = await member.AcquireRunnerAsync(cancellationToken).ConfigureAwait(false);

        bool failed = false;
        using TurnDriver driver = new(null, new TurnUsageLedger(),
            notice =>
            {
                if (notice.Kind is ETurnNotice.Failed or ETurnNotice.Refused) failed = true;
            });
        ApprovalResolver waitForUser = NestedApprovalResolver.Create(true, member.SessionId,
            SessionApprovalRegistry.Instance, ApprovalTimeout, MaxDeniedApprovalRounds, cancellationToken,
            () => ApprovalWaitingNotifier?.Invoke(member))!;
        // 群本身也挂「等审批」：左栏、导航角标、托盘认的是群那一行，成员会话不进左栏。
        // 成员自己那份由 TurnDriver 登记（右栏成员列表认它）
        async Task<IReadOnlyList<ChatMessage>> Resolver(IReadOnlyList<ToolApprovalRequestContent> requests)
        {
            if (AwayApprovals?.Invoke(member, cancellationToken) is { } away) return await away(requests).ConfigureAwait(false);

            using IDisposable waiting = SessionManager.Instance.Running.BeginApprovalWait(member.GroupId);
            return await waitForUser(requests).ConfigureAwait(false);
        }

        lease.Runner.ReplyFinishing = onReplyFinishing == null ? null : _ => onReplyFinishing();
        try
        {
            // 算有人看着：审批有人接（上面那条），他派出的子代理也照有人看着的口径跑
            await driver.RunAsync(member, lease.Runner, input, Resolver, cancellationToken, attended: true)
                .ConfigureAwait(false);
        }
        finally
        {
            lease.Runner.ReplyFinishing = null;
        }

        return !failed && !cancellationToken.IsCancellationRequested;
    }

    /// <inheritdoc />
    public Task<bool> TryInjectAsync(ChatSession member, ChatMessage message) =>
        member.Runner.TryInjectAsync([message]);

    /// <inheritdoc />
    public Task<IReadOnlyCollection<ChatMessage>> WithdrawAsync(ChatSession member,
        IReadOnlyCollection<ChatMessage> messages) =>
        member.Runner.CancelInjectionsAsync(messages);
}
