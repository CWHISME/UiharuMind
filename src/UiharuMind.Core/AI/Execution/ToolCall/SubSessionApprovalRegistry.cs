/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.ToolCall;

/// <summary>
/// 子会话审批的登记处：嵌套审批递不到派活者的回应口（父转录器本轮清单恒为空），
/// 于是改走这里——Core 侧登记等待，子会话窗口画出审批卡并把卡的回应接过来，
/// 超时或轮次取消则按拒绝收口。
///
/// 存在的理由：审批请求天生在 Core 的轮次循环里（<c>TurnDriver</c> 原地 await），
/// 而点按钮的手在 UI。两边只通过「子会话标识 + 请求对象」相认，不互相持有。
/// 无人值守从不到这里：上游的拒绝策略当场回应，轮不到等待。
///
/// <b>相认是双向的</b>：登记与画卡分别发生在两个线程上（内容流转发到界面是 Post 出去的），
/// 谁先谁后都有可能。所以画卡那一侧调 <see cref="TryAdopt"/>，登记这一侧发
/// <see cref="PendingAdded"/> 让界面回头再认一遍——任一顺序都接得上。
/// </summary>
public sealed class SubSessionApprovalRegistry
{
    /// <summary>进程内唯一登记处</summary>
    public static SubSessionApprovalRegistry Instance { get; } = new();

    private readonly object _gate = new();
    private readonly List<PendingApproval> _pending = new(); //已登记、尚未决出全部回应的

    /// <summary>
    /// 有新的嵌套审批登记进来了（参数为子会话标识）。子会话窗口据此把已经画出来的审批卡
    /// 再认领一遍——卡片先于登记诞生时，只靠画卡那一下是认不上的。
    ///
    /// ⚠️ <b>来自后台线程</b>（子代理不在 UI 线程上跑），订阅方自行 marshal。
    /// </summary>
    public event Action<string>? PendingAdded;

    /// <summary>一次待决的嵌套审批。回应先到先得，认领多少次都无妨（后到的落空，卡片保持待决态）</summary>
    private sealed class PendingApproval(string sessionId, ToolApprovalRequestContent request)
    {
        private readonly TaskCompletionSource<ChatMessage> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string SessionId { get; } = sessionId;

        /// <summary>审批请求（与内容流里的是同一对象，按引用相认）</summary>
        public ToolApprovalRequestContent Request { get; } = request;

        public Task<ChatMessage> Response => _completion.Task;

        /// <summary>写入用户决定（只认第一次）</summary>
        public bool TrySetResult(ChatMessage message) => _completion.TrySetResult(message);
    }

    /// <summary>
    /// 等待一批嵌套审批的全部决定。调用方是子代理的轮次循环（见 <c>NestedApprovalResolver</c>）。
    /// </summary>
    /// <param name="sessionId">子会话标识</param>
    /// <param name="requests">本轮新增的审批请求</param>
    /// <param name="timeout">无人点选时的等待上限，到期按拒绝收口</param>
    /// <param name="cancellationToken">轮次取消（用户点停止/墙钟超时）时按拒绝收口</param>
    /// <returns>与请求一一对应的回应消息，可直接做下一轮输入</returns>
    public async Task<IReadOnlyList<ChatMessage>> WaitForDecisionsAsync(string sessionId,
        IReadOnlyList<ToolApprovalRequestContent> requests, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        List<PendingApproval> entries = requests.Select(x => new PendingApproval(sessionId, x)).ToList();
        lock (_gate)
        {
            _pending.AddRange(entries);
        }

        PendingAdded?.Invoke(sessionId); //卡片可能已经画出来了,喊一声让它回头认领

        try
        {
            using CancellationTokenSource timeoutSource = new(timeout);
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutSource.Token);
            try
            {
                await Task.WhenAll(entries.Select(x => x.Response)).WaitAsync(linked.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 超时与取消都走拒绝收口，区别只在理由
            }

            bool timedOut = timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            foreach (PendingApproval entry in entries.Where(x => !x.Response.IsCompleted))
            {
                entry.TrySetResult(DenyMessage(entry.Request,
                    timedOut ? TimeoutReason(timeout) : StopReason));
            }

            return entries.Select(x => x.Response.Result).ToList();
        }
        finally
        {
            lock (_gate)
            {
                foreach (PendingApproval entry in entries) _pending.Remove(entry);
            }
        }
    }

    /// <summary>
    /// 界面认领：子会话窗口为一张审批卡调用，卡上点出来的决定即这次嵌套审批的回应。
    ///
    /// <b>可以重复认领</b>——切走会话再切回会重画一张卡，多窗口也各画一张，
    /// 决定先到先得（后到的落空，那张卡保持待决态，轮末统一按拒绝收视觉）。
    /// </summary>
    /// <param name="sessionId">窗口当前会话</param>
    /// <param name="request">卡片背后的请求（与登记的是同一对象）</param>
    /// <param name="decision">卡片的回应任务；完成即算用户做出决定</param>
    /// <returns>是否认领到了登记项（父会话自己的审批卡认不到，原样走父轮次的回应口）</returns>
    public bool TryAdopt(string sessionId, ToolApprovalRequestContent request, Task<ChatMessage> decision)
    {
        PendingApproval? entry;
        lock (_gate)
        {
            entry = _pending.FirstOrDefault(x =>
                string.Equals(x.SessionId, sessionId, StringComparison.Ordinal)
                && ReferenceEquals(x.Request, request));
        }

        if (entry == null) return false;

        _ = AdoptAsync(entry, decision);
        return true;
    }

    /// <summary>卡的任务只会完成不会抛（只 TrySetResult）；兜底不让等待链断在这里</summary>
    private static async Task AdoptAsync(PendingApproval entry, Task<ChatMessage> decision)
    {
        try
        {
            entry.TrySetResult(await decision.ConfigureAwait(false));
        }
        catch
        {
            // 忽略：决定没能给出，等待方自会按超时收口
        }
    }

    private static ChatMessage DenyMessage(ToolApprovalRequestContent request, string reason) =>
        new(ChatRole.User, new[] { ToolApprovalResponseFactory.Create(request, EApprovalDecision.Deny, reason) });

    private const string StopReason = "The run was stopped before this approval was answered.";

    private static string TimeoutReason(TimeSpan timeout) =>
        $"Nobody answered this approval within {timeout.TotalMinutes:0.#} minute(s); denied automatically. " +
        "Do not retry it; work around it or stop.";
}
