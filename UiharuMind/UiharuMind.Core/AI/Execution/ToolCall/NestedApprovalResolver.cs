/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.ToolCall;

/// <summary>
/// 子代理那一轮的审批通道：先递给派活者的回应口，接不住就登记到子会话等用户去那边点选。
///
/// 为什么要有这一层：嵌套审批冒到派活者回应口时，父转录器本轮清单恒为空（这些请求没经过它），
/// 回应恒为空——<c>TurnDriver</c> 会当场结束子代理那一轮，调用永无结果、历史留孤儿。
/// 所以空回应不是答案，而是「该换个人问」的信号。
///
/// 与无头那条路（<c>InProcessSchedulerBackend.DenyUnauthorizedApprovals</c>）同形：
/// 一次委派用一个，自带轮次计数。
/// </summary>
internal static class NestedApprovalResolver
{
    /// <summary>
    /// 造一个嵌套审批通道。
    /// </summary>
    /// <param name="parentResolver">派活者那一轮的回应口；为 null 表示这一跑不进审批轮次</param>
    /// <param name="sessionId">子会话标识（登记与日志用）</param>
    /// <param name="registry">登记处</param>
    /// <param name="timeout">无人点选时的等待上限，到期按拒绝收口</param>
    /// <param name="maxDeniedRounds">
    /// 连续「一条都没批准」的轮次上限。<b>计的是空转，不是次数</b>——用户每批准一次就清零，
    /// 否则老老实实点了几次允许的长任务会被自己的计数器掐掉。
    /// 到顶返回空让轮次正常结束，防模型执意重试同一个动作烧轮次。
    /// </param>
    /// <param name="cancellationToken">轮次取消（用户点停止/墙钟超时）时按拒绝收口</param>
    /// <returns>审批通道；<paramref name="parentResolver"/> 为 null 时同样返回 null</returns>
    public static ApprovalResolver? Create(ApprovalResolver? parentResolver, string sessionId,
        SubSessionApprovalRegistry registry, TimeSpan timeout, int maxDeniedRounds,
        CancellationToken cancellationToken)
    {
        if (parentResolver == null) return null;

        int deniedRounds = 0;
        return async requests =>
        {
            IReadOnlyList<ChatMessage> responses = await parentResolver(requests).ConfigureAwait(false);
            if (responses.Count > 0 || requests.Count == 0) return responses;

            if (deniedRounds >= maxDeniedRounds)
            {
                Log.Warning($"Sub-agent turn '{sessionId}' hit the {maxDeniedRounds}-round "
                            + "nested-approval limit; ending the run.");
                return [];
            }

            Log.Debug($"Sub-agent turn '{sessionId}' waiting on {requests.Count} nested approval(s).");
            IReadOnlyList<ChatMessage> decisions = await registry
                .WaitForDecisionsAsync(sessionId, requests, timeout, cancellationToken)
                .ConfigureAwait(false);

            deniedRounds = AllDenied(decisions) ? deniedRounds + 1 : 0;
            return decisions;
        };
    }

    /// <summary>
    /// 这一轮是不是一条都没批准。口径取「全是明确拒绝」而不是「没看到批准」——
    /// 「本会话总是允许」由框架的扩展方法构造，内容类型未必是这里认得的那个，
    /// 认不出时按没拒绝算，宁可多给一轮也不要把正在配合的用户掐掉。
    /// </summary>
    private static bool AllDenied(IReadOnlyList<ChatMessage> decisions) =>
        decisions.Count > 0 && decisions.SelectMany(x => x.Contents)
            .All(x => x is ToolApprovalResponseContent { Approved: false });
}
