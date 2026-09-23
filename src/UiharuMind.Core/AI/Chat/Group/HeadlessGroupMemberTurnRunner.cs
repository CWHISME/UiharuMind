using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 成员一轮的默认跑法：与定时任务同一条无头编排（<see cref="TurnDriver"/>，没有渲染落点），
/// 运行态登记、取消收尾、交接文档一并到手。
///
/// 审批<b>一律拒绝</b>：骨架阶段群视图还接不住成员的审批卡（ADR 0046 未决）。
/// 权限档照常起作用——工作区内的读写在自动编辑档本来就不问，被拒的是 shell 与越界写入。
/// </summary>
public sealed class HeadlessGroupMemberTurnRunner : IGroupMemberTurnRunner
{
    private const int MaxApprovalRounds = 3; //同一轮里被拒到第几次就收口,免得模型换着花样一直要

    private const string DenialReason =
        "Nobody in this group chat can approve this call right now. Use an approach that needs no approval, " +
        "or say in the group what you need.";

    /// <inheritdoc />
    public async Task<bool> RunAsync(ChatSession member, ChatMessage input, CancellationToken cancellationToken)
    {
        await member.Runner.AttachAsync(member, cancellationToken).ConfigureAwait(false);

        bool failed = false;
        using TurnDriver driver = new(null, new TurnUsageLedger(),
            notice =>
            {
                if (notice.Kind == ETurnNotice.Failed) failed = true;
            });
        await driver.RunAsync(member, member.Runner, input, DenyAll(member), cancellationToken, attended: false)
            .ConfigureAwait(false);

        return !failed && !cancellationToken.IsCancellationRequested;
    }

    /// <inheritdoc />
    public Task<bool> TryInjectAsync(ChatSession member, ChatMessage message) =>
        member.Runner.TryInjectAsync([message]);

    private static ApprovalResolver DenyAll(ChatSession member)
    {
        int round = 0;
        return requests =>
        {
            if (++round > MaxApprovalRounds) return Task.FromResult<IReadOnlyList<ChatMessage>>([]);

            IReadOnlyList<ChatMessage> denials = requests
                .Select(request =>
                {
                    Log.Warning($"Group member '{member.Title}' ({member.SessionId}) denied " +
                                $"{(request.ToolCall as FunctionCallContent)?.Name ?? "a tool call"}: nobody to ask.");
                    return new ChatMessage(ChatRole.User,
                        new List<AIContent> { request.CreateResponse(approved: false, reason: DenialReason) });
                })
                .ToList();
            return Task.FromResult(denials);
        };
    }
}
