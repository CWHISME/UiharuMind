/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.ToolCall;

/// <summary>
/// 给「调用发出去了、结果永远不会来」的工具调用补一条取消结果。
///
/// 中途停止（以及轮次撞网络失败）时这种调用是必然产物：模型那次请求本身成功返回了
/// （助手消息带 tool_call，逐次服务调用当场落盘），随后在<b>执行工具</b>的过程中被掐断，
/// 结果消息因此从未产生。
/// 历史里于是留下一条孤儿 tool_call——OpenAI 与 Anthropic 都要求带 tool_calls 的助手消息
/// 必须有配对的结果，否则整个请求 400，这个会话从此发不出话。
///
/// 标记写在<b>结果正文</b>里而不是 <see cref="FunctionResultContent.Exception"/>：
/// 那个属性带 <c>[JsonIgnore]</c>，存进会话文件再读回来就没了，卡片会重新显示成绿色的成功态。
/// 正文同时也是给模型看的——它下一轮该知道自己被打断过。
/// </summary>
public static class ToolCallCancellation
{
    private const string Marker = "[cancelled]";

    /// <summary>补写的结果正文。英文与历史里其它模型可见的占位文本同口径</summary>
    public const string ResultText = Marker + " The user stopped this turn before the tool returned.";

    /// <summary>
    /// 轮次失败（撞网络/限流，不是用户点的停止）时补写的结果正文。
    /// 与 <see cref="ResultText"/> 共用标记：对界面与模型而言两者是同一件事——
    /// 这次调用没有结果，区别只在原因，而原因值得如实告诉模型。
    /// </summary>
    public const string FailureResultText = Marker + " This turn failed before the tool returned.";

    /// <summary>
    /// 审批未决时补写的结果正文：调用发出去了，但审批请求在轮次结束前始终没人回应，
    /// 函数根本没执行。共用 <c>[cancelled]</c> 标记——卡片按失败显示（它确实没跑完），
    /// 下次打开该会话也不会把这条当成功的结果读。
    ///
    /// 目前唯一的调用方是子代理的正常结束路径：嵌套审批冒到派活者回应口后被静默丢掉
    /// （派活者转录器本轮清单是空的），轮次正常结束、没人补过结果。
    /// </summary>
    public const string ApprovalUnansweredResultText = Marker
        + " This tool call never ran: its approval request was not answered before the turn ended.";

    /// <summary>
    /// 审批<b>明确被拒</b>（用户拒绝、超时按拒绝收口）时补写的结果正文。与
    /// <see cref="ApprovalUnansweredResultText"/> 的差别在语义：那边是「等到轮次结束也没人答」，
    /// 这边是「答案给出来了：不行」——都共用 <c>[cancelled]</c> 标记，卡片按失败显示，
    /// 下次打开该会话也不会把这条当成功的结果读。
    /// </summary>
    public const string DeniedResultText = Marker
        + " This tool call never ran: its approval was denied before execution.";

    /// <summary>
    /// 判断一条工具结果是否为取消补写的
    /// </summary>
    /// <param name="result">工具结果</param>
    /// <returns>是否取消</returns>
    public static bool IsCancelled(FunctionResultContent result)
    {
        return result.Result?.ToString()?.StartsWith(Marker, StringComparison.Ordinal) == true;
    }

    /// <summary>
    /// 给会话末尾没等到结果的工具调用补上取消结果并落盘。
    ///
    /// <b>只处理末尾那一轮</b>：从最后一条往前走，遇到既不是工具结果、也不是带调用的助手消息就停。
    /// 这样补出来的结果紧跟在它的调用之后，位置天然正确。历史更早处若有遗留的孤儿（本方法上线前
    /// 留下的），追加到末尾反而会打乱顺序，所以只记一条日志、不动它。
    /// </summary>
    /// <param name="session">当前会话</param>
    /// <param name="resultText">补写的结果正文，默认按「用户停止」口径</param>
    /// <returns>补写的条数</returns>
    public static int CloseUnansweredAtTail(ChatSession session, string? resultText = null)
    {
        List<string> unanswered = FindUnansweredAtTail(session.History);
        if (unanswered.Count == 0) return 0;

        string text = resultText ?? ResultText;
        int before = session.History.Count;
        foreach (string callId in unanswered)
        {
            session.History.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, text)])
            {
                CreatedAt = DateTimeOffset.Now,
            });
        }

        session.SaveAppended(before);
        Log.Debug($"Closed {unanswered.Count} unanswered tool call(s) after the turn was interrupted.");
        return unanswered.Count;
    }

    /// <summary>
    /// 找出末尾那一轮里没有配对结果的工具调用
    /// </summary>
    /// <param name="history">会话历史</param>
    /// <returns>调用标识，按调用顺序</returns>
    internal static List<string> FindUnansweredAtTail(IReadOnlyList<ChatMessage> history)
    {
        List<string> calls = [];
        HashSet<string> answered = [];

        for (int i = history.Count - 1; i >= 0; i--)
        {
            ChatMessage message = history[i];
            List<string> callsHere = [];
            bool isResult = false;

            foreach (AIContent content in message.Contents)
            {
                switch (content)
                {
                    case FunctionResultContent result:
                        answered.Add(result.CallId);
                        isResult = true;
                        break;
                    case FunctionCallContent call:
                        callsHere.Add(call.CallId);
                        break;
                }
            }

            //消息是倒着扫的,但一条消息内部的并行调用是正序收集的——整块前插才能还原成调用顺序
            calls.InsertRange(0, callsHere);

            //越过了本轮的工具往返,再往前的调用都早已有结果(或是与本次停止无关的历史遗留)
            if (!isResult && callsHere.Count == 0) break;
        }

        return calls.Where(x => !answered.Contains(x)).ToList();
    }

    /// <summary>
    /// 审批回环拿到<b>明确拒绝</b>的决定后，把对应调用补上「被拒」结果并落盘。
    ///
    /// 为什么需要它：被拒的调用在 MFA 审批闸上不执行，永远不会有 <see cref="FunctionResultContent"/>，
    /// 拒绝响应只是作为下一轮模型输入——历史里于是留下孤儿 tool_call（OpenAI/Anthropic 都要求配对，
    /// 严格服务端直接 400，这个会话从此发不出话）。<see cref="CloseUnansweredAtTail"/> 只补末尾，
    /// 夹在中间的孤儿（模型被拒后继续跑别的调用）只有这里收。会话重放时卡片也因此显示
    /// 「被拒」而不是「历史里没有这次调用的结果」。
    ///
    /// 插入位置跟在该调用所在助手消息之后、同批已落盘结果之后——此刻下一轮尚未开始，
    /// 那批结果之后就是安全插入点。
    /// </summary>
    /// <param name="session">当前会话</param>
    /// <param name="requests">本轮审批请求</param>
    /// <param name="decisions">审批决定，与 <paramref name="requests"/> 一一对应</param>
    /// <returns>补写的条数</returns>
    public static int CloseDeniedCalls(ChatSession session,
        IReadOnlyList<ToolApprovalRequestContent> requests,
        IReadOnlyList<ChatMessage> decisions)
    {
        int inserted = AppendDeniedCallResults(session.History, requests, decisions);
        if (inserted > 0) session.Save();
        return inserted;
    }

    /// <summary>纯历史操作，供 <see cref="CloseDeniedCalls"/> 调用与单测</summary>
    internal static int AppendDeniedCallResults(List<ChatMessage> history,
        IReadOnlyList<ToolApprovalRequestContent> requests,
        IReadOnlyList<ChatMessage> decisions)
    {
        if (requests.Count == 0) return 0;
        if (requests.Count != decisions.Count)
        {
            // 按位置配对的前提不成立（认不到的请求会少一条）。宁可整体不动也别按错位去补：
            // 但那批调用会留下孤儿 tool_call，日志点名便于后续按 CallId 关联时回溯
            Log.Warning($"CloseDeniedCalls: {requests.Count} request(s) vs {decisions.Count} decision(s); " +
                        "denied-result patching skipped for this round.");
            return 0;
        }

        int inserted = 0;
        for (int i = 0; i < requests.Count; i++)
        {
            // 只收「明确拒绝」：批准与「本会话总是允许」（AlwaysApprove 包装）都不能判成拒绝——
            // 前者真执行了（会有结果）；后者是框架 wrapper、会被 OfType 滤掉，空序列 All(...) 判真，
            // 会把已批准即将执行的调用误写成 denied（实机踩过）。
            List<ToolApprovalResponseContent> responses = decisions[i].Contents
                .OfType<ToolApprovalResponseContent>()
                .ToList();
            bool explicitlyDenied = responses.Count > 0 && responses.All(x => !x.Approved);
            if (!explicitlyDenied) continue;

            if (requests[i].ToolCall is not FunctionCallContent call) continue;
            string callId = call.CallId;
            if (history.Any(m => m.Contents.OfType<FunctionResultContent>()
                    .Any(r => r.CallId == callId))) continue;

            int assistantIndex = -1;
            for (int j = history.Count - 1; j >= 0; j--)
            {
                if (history[j].Contents.OfType<FunctionCallContent>().Any(c => c.CallId == callId))
                {
                    assistantIndex = j;
                    break;
                }
            }

            ChatMessage resultMessage = new(ChatRole.Tool,
                [new FunctionResultContent(callId, DeniedResultText)])
            {
                CreatedAt = DateTimeOffset.Now,
            };
            if (assistantIndex < 0)
            {
                // 找不到（历史还没写到那一步的极端情况）：退化为追加到末尾，至少配对存在
                history.Add(resultMessage);
            }
            else
            {
                // 该调用所在助手消息之后，跳过同批已落盘的结果，找到插入点
                int insertAt = assistantIndex + 1;
                while (insertAt < history.Count
                       && history[insertAt].Contents.OfType<FunctionResultContent>().Any())
                {
                    insertAt++;
                }
                history.Insert(insertAt, resultMessage);
            }
            inserted++;
        }
        return inserted;
    }
}
