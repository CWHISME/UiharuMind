using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.ToolCall;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Chat.Group.Away;

/// <summary>
/// 请化身判一条审批
/// </summary>
/// <param name="Avatar">化身会话</param>
/// <param name="GroupLog">群流水此刻的拷贝</param>
/// <param name="RequesterName">请求审批的成员名</param>
/// <param name="Call">那次工具调用</param>
public sealed record GroupAwayApprovalAsk(ChatSession Avatar, IReadOnlyList<ChatMessage> GroupLog, string RequesterName,
    FunctionCallContent Call);

/// <summary>化身的判定</summary>
/// <param name="Approved">批了为 true</param>
/// <param name="Reason">一句理由</param>
public sealed record GroupAwayApprovalVerdict(bool Approved, string Reason);

/// <summary>
/// 离席期间的审批通道（ADR 0055）：用户不在，审批由化身点，不让一张卡把整波卡上十分钟。
/// <list type="bullet">
/// <item>越界写入当场拒绝：硬规则要的是「工作区外的盘模型不能自己动」，化身也不替用户点（ADR 0010）</item>
/// <item>化身自己的调用自己放行——成员同样的请求它都会批</item>
/// <item>其余交给化身判一次（旁路调用，不算一轮），判不出来按拒绝</item>
/// </list>
/// 每一条都记账，进离席回执
/// </summary>
public sealed class GroupAwayApprover
{
    private const int MaxDeniedRounds = 3; //连着几轮一条都没批：收掉这一轮，免得模型换着花样一直要

    private readonly Func<GroupAwayApprovalAsk, CancellationToken, Task<GroupAwayApprovalVerdict>> _judge;
    private readonly Func<ChatSession, IReadOnlyList<ChatMessage>> _historyOf;

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="judge">请化身判一条审批；null 走 <see cref="AvatarApprovalJudge.JudgeAsync"/></param>
    /// <param name="historyOf">取群流水的拷贝；null 走 <see cref="GroupChatCoordinator.HistorySnapshot"/></param>
    public GroupAwayApprover(Func<GroupAwayApprovalAsk, CancellationToken, Task<GroupAwayApprovalVerdict>>? judge = null,
        Func<ChatSession, IReadOnlyList<ChatMessage>>? historyOf = null)
    {
        _judge = judge ?? AvatarApprovalJudge.JudgeAsync;
        _historyOf = historyOf ?? GroupChatCoordinator.Instance.HistorySnapshot;
    }

    /// <summary>
    /// 为请求方这一轮造一个审批通道
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <param name="avatar">化身会话</param>
    /// <param name="requester">请求审批的成员（或化身自己）</param>
    /// <param name="record">记一条账</param>
    /// <param name="cancellationToken">请求方这一轮被停时取消</param>
    /// <returns>审批通道</returns>
    public ApprovalResolver Create(ChatSession group, ChatSession avatar, ChatSession requester,
        Action<GroupAwayApproval> record, CancellationToken cancellationToken)
    {
        int deniedRounds = 0;
        Party party = new(group, avatar, requester, GroupSceneSource.SpeakerNameOf(requester));
        return async requests =>
        {
            if (requests.Count == 0 || deniedRounds >= MaxDeniedRounds) return [];

            List<ChatMessage> responses = [];
            bool anyApproved = false;
            foreach (ToolApprovalRequestContent request in requests)
            {
                GroupAwayApprovalVerdict verdict = request.ToolCall is FunctionCallContent call
                    ? await DecideAsync(party, call, cancellationToken).ConfigureAwait(false)
                    : new GroupAwayApprovalVerdict(false, "认不出这次调用");
                anyApproved |= verdict.Approved;
                record(new GroupAwayApproval(party.RequesterName, NestedApprovalResolver.Describe(request), verdict.Approved,
                    verdict.Reason));
                responses.Add(new ChatMessage(ChatRole.User, [
                    ToolApprovalResponseFactory.Create(request,
                        verdict.Approved ? EApprovalDecision.Once : EApprovalDecision.Deny, verdict.Reason),
                ]));
            }

            deniedRounds = anyApproved ? 0 : deniedRounds + 1;
            return responses;
        };
    }

    private async Task<GroupAwayApprovalVerdict> DecideAsync(Party party, FunctionCallContent call,
        CancellationToken cancellationToken)
    {
        if (ApprovalModeMapper.IsOutOfWorkspaceWrite(call, AgentBuildProfile.PathResolverOf(party.Requester)))
        {
            return new GroupAwayApprovalVerdict(false, "越界写入：离席中不替用户点，留给用户本人");
        }

        if (party.Requester.SessionId == party.Avatar.SessionId) return new GroupAwayApprovalVerdict(true, "化身自己的操作");

        try
        {
            GroupAwayApprovalAsk ask = new(party.Avatar, _historyOf(party.Group), party.RequesterName, call);
            return await _judge(ask, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Log.Warning($"Group avatar failed to judge an approval: {e.Message}");
            return new GroupAwayApprovalVerdict(false, "化身没判出来，按拒绝处理");
        }
    }

    // 一张审批牵扯的几方：哪个群、谁替用户判、谁在要
    private sealed record Party(ChatSession Group, ChatSession Avatar, ChatSession Requester, string RequesterName);
}
