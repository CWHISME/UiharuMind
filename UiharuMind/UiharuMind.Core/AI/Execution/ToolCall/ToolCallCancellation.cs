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
/// 中途停止时这种调用是必然产物：模型那次请求本身成功返回了（助手消息带 tool_call，
/// 逐次服务调用当场落盘），随后在<b>执行工具</b>的过程中被掐断，结果消息因此从未产生。
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
    /// <returns>补写的条数</returns>
    public static int CloseUnansweredAtTail(ChatSession session)
    {
        List<string> unanswered = FindUnansweredAtTail(session.History);
        if (unanswered.Count == 0) return 0;

        int before = session.History.Count;
        foreach (string callId in unanswered)
        {
            session.History.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, ResultText)])
            {
                CreatedAt = DateTimeOffset.Now,
            });
        }

        session.SaveAppended(before);
        Log.Debug($"Closed {unanswered.Count} unanswered tool call(s) after the turn was stopped.");
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
}
