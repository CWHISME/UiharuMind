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
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.ToolCall;

/// <summary>无人值守时对一条审批的判定：批不批、以及送给模型的那句理由</summary>
/// <param name="Approved">批了为 true；false 按拒绝收口</param>
/// <param name="Reason">
/// 理由，会送给模型。说清是「无人值守」而不是「用户不同意」，模型才不会反复请求同一个动作
/// </param>
internal readonly record struct UnattendedVerdict(bool Approved, string Reason);

/// <summary>
/// 无人值守的审批通道：冒到这里的请求<b>一条都不问</b>，按判据当场收口。
///
/// 凡是「没人会来点那个按钮」的轮次都走这里，只差判据：定时任务的无头执行一律拒、
/// 群离席交给化身判、目标模式的自动同意一律批（<see cref="Chat.Single.GoalMode.GoalModeApproval"/>）。
/// 形状一样，共用的这几件正是无人值守的命门：
/// <list type="bullet">
/// <item>逐条记日志——那一跑完了只剩一个状态，「为什么什么都没干」除了日志无处可查</item>
/// <item>轮次上限——防备模型换着花样一直要，批也批不完、拒也拒不完</item>
/// <item>回应带理由、逐次 Once——不静默挂起等待，也不悄悄给成「本会话总是允许」</item>
/// </list>
/// 通道自带计数，<b>一次执行用一个</b>（一个任务的一跑、一轮委派、一个目标模式唤醒轮）。
/// </summary>
internal static class UnattendedApprovals
{
    /// <summary>连续「一条都没批」的轮次上限默认值：与嵌套审批、群离席原口径一致</summary>
    internal const int DefaultMaxDeniedRounds = 3;

    private static readonly IReadOnlyList<ChatMessage> Empty = [];

    /// <summary>
    /// 造一个无人值守审批通道
    /// </summary>
    /// <param name="owner">日志与限位时指名道姓的那一位（任务、群、目标模式会话）</param>
    /// <param name="decide">逐条判定；判据是同步的也要包一层，为的是化身那种要旁路调用的</param>
    /// <param name="maxRounds">
    /// 总轮次上限，到顶返回空、运行循环据此收工。给「一律批」的策略用——
    /// 那种策略一条都不会被拒，<paramref name="maxDeniedRounds"/> 永远不触发
    /// </param>
    /// <param name="maxDeniedRounds">连续「一条都没批」的轮次上限；每批一条就清零</param>
    /// <param name="onDecided">每决出一条调一次（离席回执要逐条记账）</param>
    /// <returns>审批通道</returns>
    internal static ApprovalResolver Create(string owner,
        Func<ToolApprovalRequestContent, Task<UnattendedVerdict>> decide, int? maxRounds = null,
        int? maxDeniedRounds = DefaultMaxDeniedRounds,
        Action<ToolApprovalRequestContent, UnattendedVerdict>? onDecided = null)
    {
        int round = 0;
        int deniedStreak = 0;
        return async requests =>
        {
            if (requests.Count == 0) return Empty;

            if (maxRounds is { } limit && ++round > limit)
            {
                Log.Warning($"{owner} hit the {limit}-round unattended approval limit; ending the run.");
                return Empty;
            }

            if (maxDeniedRounds is { } denied && deniedStreak >= denied)
            {
                Log.Warning($"{owner} hit the {denied}-round unattended approval limit "
                            + "with nothing approved; ending the run.");
                return Empty;
            }

            List<ChatMessage> responses = new(requests.Count);
            bool anyApproved = false;
            foreach (ToolApprovalRequestContent request in requests)
            {
                UnattendedVerdict verdict = await decide(request).ConfigureAwait(false);
                anyApproved |= verdict.Approved;
                Log.Warning($"{owner} {(verdict.Approved ? "approved" : "denied")} {Describe(request)} "
                            + $"(unattended, nobody to ask): {verdict.Reason}");
                responses.Add(new ChatMessage(ChatRole.User,
                [
                    ToolApprovalResponseFactory.Create(request,
                        verdict.Approved ? EApprovalDecision.Once : EApprovalDecision.Deny, verdict.Reason)
                ]));
                onDecided?.Invoke(request, verdict);
            }

            deniedStreak = anyApproved ? 0 : deniedStreak + 1;
            return responses;
        };
    }

    /// <summary>
    /// 无人值守优先、回落到别处：开关开着走作用域里冻结的通道，关着（或本执行中途撤销、
    /// 通道到了上限）交给 <paramref name="fallback"/>，那边也没有就按未决收。
    /// 唤醒轮（回落到窗口）与子代理（回落到子会话的点选）共用这一份形状
    /// </summary>
    /// <param name="scope">本执行的无人值守作用域</param>
    /// <param name="fallback">回落的回应口，每批现取；没有为 null</param>
    /// <param name="settled">无人值守决出之后调一次（收卡、记账）；抛了只记日志，不影响已决出的判据</param>
    /// <returns>回应口；此刻开关关着且没有回落为 null（调用方按无头收口）</returns>
    internal static ApprovalResolver? Prefer(UnattendedScope scope, Func<ApprovalResolver?> fallback,
        Action<IReadOnlyList<ToolApprovalRequestContent>, IReadOnlyList<ChatMessage>>? settled = null)
    {
        if (scope.ForBatch() == null && fallback() == null) return null;
        return async requests =>
        {
            if (scope.ForBatch() is { } active)
            {
                IReadOnlyList<ChatMessage> responses = await active(requests).ConfigureAwait(false);
                if (responses.Count > 0)
                {
                    try
                    {
                        settled?.Invoke(requests, responses);
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"Unattended approval settled notice failed: {e.Message}");
                    }

                    return responses;
                }

                // 通道到了上限：有回落就交给它（人可能就在），没有按未决收
            }

            if (fallback() is { } next) return await next(requests).ConfigureAwait(false);
            return Empty;
        };
    }

    /// <summary>一句话说清这次调用是谁、动哪儿（日志点名与事后报告同一口径）</summary>
    /// <param name="request">审批请求</param>
    /// <returns>描述</returns>
    internal static string Describe(ToolApprovalRequestContent request)
    {
        if (request.ToolCall is not FunctionCallContent call) return "an unknown tool call";
        string? detail = ApprovalModeMapper.ExtractCommand(call.Arguments) ??
                         ApprovalModeMapper.ExtractFilePath(call.Arguments);
        return string.IsNullOrWhiteSpace(detail) ? call.Name : $"{call.Name}[{detail}]";
    }
}
