/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Chat;

/// <summary>交回结果</summary>
public enum EHandoffOutcome
{
    /// <summary>作为新的一条追加进派活者历史</summary>
    Appended,

    /// <summary>原地替换了上一份（模型还没读过它）</summary>
    Replaced,

    /// <summary>这不是子会话</summary>
    NotASubSession,

    /// <summary>派活者已不存在</summary>
    ParentMissing,

    /// <summary>派活者正在跑，此刻写它的历史会与落盘交错</summary>
    ParentBusy,

    /// <summary>子会话里还没有可交回的结论</summary>
    NothingToReport,
}

/// <summary>
/// 把子会话的<b>后续报告</b>交回派活者。
///
/// 存在的理由：子代理那次工具调用结束之后，用户在子会话里接着跑出来的结论<b>无处可回</b>
/// ——工具结果早已定型。它落成派活者历史里的一条真消息（落盘、供给模型，只是渲染不同），
/// 形状同旁白；不走注入队列，理由见 <see cref="ChatMessageAnnotations.SubAgentReport"/>。
///
/// <b>一个子会话在派活者那里不是想留几条留几条</b>（见 ADR 0021 与 CONTEXT.md「后续报告」）：
/// 上一份若还停在历史末尾（模型没读过），再交一次就原地替换；若它后面已经有新的轮次
/// （模型已据此回应过），才追加一条并声明它修正了先前那份。抹掉已被消费的那份，
/// 等于让派活者的那段回应变得没有来由。
/// </summary>
public static class SubAgentReportHandoff
{
    /// <summary>
    /// 把子会话最新的结论交回派活者
    /// </summary>
    /// <param name="subSession">子会话</param>
    /// <param name="interruption">
    /// 这次交回是<b>被中止</b>的（进程退出时后台委派还没跑完）。非空时即使子会话里一个字都没有
    /// 也要交回——「它没干完」本身就是派活者必须知道的事，否则父会话里那条「已派出」永远没有下文。
    /// </param>
    /// <param name="conclusion">
    /// 要交回的结论正文。<b>后台委派必须给这一份</b>：委派跑完时攒出来的报告带着
    /// 「用户中止了」「超时了」「有几个调用因审批未决没跑成」这些注记，而从子会话历史里
    /// 现捞「最后一段助手正文」把它们全丢了——主 agent 于是把没干完的活当成干完了。
    /// 为空时才回落到现捞（用户手动点「交回主 agent」走的就是那条）。
    /// </param>
    /// <returns>交回结果</returns>
    public static EHandoffOutcome Submit(ChatSession subSession, string? interruption = null,
        string? conclusion = null)
    {
        if (!subSession.IsSubSession) return EHandoffOutcome.NotASubSession;

        ChatSession? parent = SessionManager.Instance.Load(subSession.ParentSessionId!);
        if (parent == null) return EHandoffOutcome.ParentMissing;
        // 派活者正在跑时不写:那一轮的历史由框架逐次服务调用追加,此刻插一条进去会与它交错
        if (SessionManager.Instance.Running.IsBusy(parent.SessionId)) return EHandoffOutcome.ParentBusy;

        conclusion = string.IsNullOrWhiteSpace(conclusion) ? LastAssistantText(subSession) : conclusion.Trim();
        if (conclusion.Length == 0 && interruption == null) return EHandoffOutcome.NothingToReport;

        (int existing, bool replaceInPlace) = ResolveSlot(parent.History, subSession.SessionId);

        ChatMessage message = BuildMessage(subSession, conclusion, supersedes: existing >= 0 && !replaceInPlace,
            interruption);
        if (replaceInPlace)
        {
            ChatMessage superseded = parent.History[existing];
            // 结论没变就什么都不做:交回是幂等的。照写不误的话派活者的历史文件要整份重写一遍,
            // 界面还得为一条一字未改的消息重建条目——用户看到的就是"点一次闪一次"
            if (string.Equals(superseded.Text, message.Text, StringComparison.Ordinal))
                return EHandoffOutcome.Replaced;

            parent.History[existing] = message;
            parent.Save(); //改的是中间那条,只能整份重写
            // 派活者的界面壳(如果开着)得知道:这一份不是它写的,不发信号它会一直显示旧的。
            // 带上被换掉的那一条,界面据此只重建那一处——整份重放会让满屏 markdown 闪一下
            parent.NotifyHistoryMessageReplaced(existing, superseded);
            return EHandoffOutcome.Replaced;
        }

        int from = parent.History.Count;
        parent.History.Add(message);
        parent.SaveAppended(from);
        return EHandoffOutcome.Appended;
    }

    /// <summary>子会话里最后一段助手正文，即它此刻的结论</summary>
    private static string LastAssistantText(ChatSession subSession)
    {
        for (int i = subSession.History.Count - 1; i >= 0; i--)
        {
            ChatMessage message = subSession.History[i];
            if (message.Role != ChatRole.Assistant) continue;

            string text = message.Text?.Trim() ?? string.Empty;
            if (text.Length > 0) return text;
        }

        return string.Empty;
    }

    /// <summary>
    /// 这次交回该落在哪儿：原地替换上一份，还是追加一条。
    ///
    /// 抽成不碰单例的纯函数是为了能单测——这段取舍（"末尾即未被消费"）不写测试，
    /// 下次重构就会被"顺手简化"成无脑追加或无脑替换，而两者各自会坏掉一半场景。
    /// </summary>
    /// <param name="parentHistory">派活者的历史</param>
    /// <param name="subSessionId">子会话标识</param>
    /// <returns>上一份的下标（没有为 -1）与是否原地替换</returns>
    internal static (int Index, bool ReplaceInPlace) ResolveSlot(IReadOnlyList<ChatMessage> parentHistory,
        string subSessionId)
    {
        for (int i = parentHistory.Count - 1; i >= 0; i--)
        {
            if (ChatMessageAnnotations.ReadSubAgentReportSession(parentHistory[i]) != subSessionId) continue;

            // 还停在末尾就说明模型没读过它，那一份留着只是垃圾；
            // 后面已经有新轮次的话，它已经被据以回应过，抹掉会让那段回应没有来由
            return (i, i == parentHistory.Count - 1);
        }

        return (-1, false);
    }

    /// <summary>
    /// 组装交回的那条消息。措辞必须<b>显式指回哪次委派</b>——派活者历史里往往已经躺着
    /// 一条矛盾的前情（那份半截报告说"没结论"），模型得看得出时序与归属
    /// </summary>
    private static ChatMessage BuildMessage(ChatSession subSession, string conclusion, bool supersedes,
        string? interruption)
    {
        if (interruption != null)
        {
            string tail = conclusion.Length > 0
                ? $"以下是它中止前已有的进展：\n\n{conclusion}"
                : "它没有产出任何结论。**不要把这次委派当成已完成。**";
            return Annotate(subSession,
                $"你先前那次委派——子会话 `{subSession.SessionId}`（{subSession.Title}）"
                + $"——{interruption}。{tail}");
        }

        string head = supersedes
            ? $"以下是子会话 `{subSession.SessionId}`（{subSession.Title}）的**进一步结论**，"
              + "它修正了先前那份后续报告："
            : $"以下是你先前那次委派——子会话 `{subSession.SessionId}`（{subSession.Title}）"
              + "——在工具调用结束之后产出的**后续结论**：";

        return Annotate(subSession, $"{head}\n\n{conclusion}");
    }

    /// <summary>盖上后续报告标记：带它的消息要落盘、要供给模型，只是渲染成旁白那一套</summary>
    private static ChatMessage Annotate(ChatSession subSession, string text) =>
        new(ChatRole.User, text)
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatMessageAnnotations.SubAgentReport] = subSession.SessionId,
            },
        };
}
