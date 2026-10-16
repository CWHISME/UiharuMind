using System;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.ToolCall;

namespace UiharuMind.Core.AI.Chat.Single.GoalMode;

/// <summary>
/// 目标模式的「自动同意审批」：这个会话的审批一条都不问、直接同意（唤醒轮没人接得住时尤其如此）。
///
/// 没有它，没开着那个会话时每张卡都按无头口径拒绝（与定时任务同形），目标就推不动；
/// 用户在设置弹窗勾选并确认过危险度后才走到这里，所以只适用于目标模式开着的会话。
///
/// 收口形状与其余无人值守策略共用 <see cref="UnattendedApprovals"/>（逐条记日志、
/// 带轮次上限、回应带理由），这里只提供判据——一律同意。
///
/// ⚠️ <b>越界写入也一并同意</b>，这与群离席那条口径不同（那边「工作区外的盘模型不能自己动」是硬规则，
/// 化身也不替用户点，见 ADR 0010/0032）。这里是产品上的取舍：目标模式推的正是工作区内外的活，
/// 一律拦下它就推不动。危险度在设置弹窗的确认文案里逐条列明，由用户当面确认过，不是漏判。
/// </summary>
public static class GoalModeApproval
{
    /// <summary>回应里带上的理由：历史与日志据此认出这是自动同意的</summary>
    public const string Reason = "Auto-approved: goal mode is running with auto-approve approvals on.";

    /// <summary>
    /// 造这个会话的无人值守审批通道。一个唤醒轮用一个：轮次上限记在通道里，跨轮次要重新取
    /// </summary>
    /// <param name="sessionId">会话标识，日志指名道姓用</param>
    /// <returns>审批通道</returns>
    public static ApprovalResolver Create(string sessionId) =>
        UnattendedApprovals.Create($"Goal mode session '{sessionId}'",
            _ => Task.FromResult(new UnattendedVerdict(true, Reason)),
            // 一律批：连拒上限永远触发不了，显式关掉，只留总轮次上限
            maxRounds: MaxRounds, maxDeniedRounds: null);

    /// <summary>
    /// 一个唤醒轮里最多收口几批审批。模型每轮都要审批、还一批比一批多，就是在空转——
    /// 目标模式本身没有保险丝，这一条是它仅有的审批侧闸门
    /// </summary>
    internal const int MaxRounds = 8;
}
