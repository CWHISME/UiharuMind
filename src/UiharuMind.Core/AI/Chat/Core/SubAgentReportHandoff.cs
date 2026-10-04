/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Tools;

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
/// 形状同旁白。派活者醒着时先插进那一轮，由 <c>BackgroundSubAgentDispatcher</c> 经 <c>SessionDelivery</c> 送（ADR 0062），
/// 这里只管写成什么样、闲着时落在哪儿。
///
/// <b>一个子会话在派活者那里不是想留几条留几条</b>（见 ADR 0021 与 CONTEXT.md「后续报告」）：
/// 上一份若还停在历史末尾（模型没读过），再交一次就原地替换；若它后面已经有新的轮次
/// （模型已据此回应过），才追加一条并声明它修正了先前那份。抹掉已被消费的那份，
/// 等于让派活者的那段回应变得没有来由。以上只对<b>回同一次来信</b>的两封成立：
/// 续聊问了新问题，回信就是新的一封，既不替换也不算更正。
/// </summary>
public static class SubAgentReportHandoff
{
    private const int QuoteLength = 30; //「回你之前发的」引多长：认得出是哪件事就够

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
    /// 现捞「最后一段助手正文」把它们全丢了——主代理于是把没干完的活当成干完了。
    /// 为空时才回落到现捞（用户手动点「交回主代理」走的就是那条）。
    /// </param>
    /// <returns>交回结果</returns>
    public static EHandoffOutcome Submit(ChatSession subSession, string? interruption = null,
        string? conclusion = null)
    {
        if (!subSession.IsSubSession) return EHandoffOutcome.NotASubSession;
        string parentSessionId = subSession.ParentSessionId!;

        // 同一父会话的多封回信可能由多个子代理并行交回，按父会话串行提交
        lock (SessionHistoryLocks.For(parentSessionId))
        {
            ChatSession? parent = SessionManager.Instance.Load(parentSessionId);
            if (parent == null) return EHandoffOutcome.ParentMissing;
            // 派活者正在跑时不写:那一轮的历史由框架逐次服务调用追加,此刻插一条进去会与它交错
            if (SessionManager.Instance.Running.IsBusy(parent.SessionId)) return EHandoffOutcome.ParentBusy;

            return Write(parent, subSession, interruption, conclusion, othersPending: 0);
        }
    }

    /// <summary>
    /// 落进派活者历史：上一封还停在末尾就原地替换，否则追加。调用方持派活者的历史锁、已确认它不在跑
    /// </summary>
    /// <param name="parent">派活者</param>
    /// <param name="subSession">子会话</param>
    /// <param name="interruption">被打断的原因，见 <see cref="Submit"/></param>
    /// <param name="conclusion">回信正文，见 <see cref="Submit"/></param>
    /// <param name="othersPending">派活者还在等几位别人的回信</param>
    /// <returns>交回结果</returns>
    internal static EHandoffOutcome Write(ChatSession parent, ChatSession subSession, string? interruption,
        string? conclusion, int othersPending)
    {
        conclusion = string.IsNullOrWhiteSpace(conclusion) ? LastAssistantText(subSession) : conclusion.Trim();
        if (conclusion.Length == 0 && interruption == null) return EHandoffOutcome.NothingToReport;

        (int existing, bool replaceInPlace) = ResolveSlot(parent.History, subSession.SessionId, RequestKey(subSession));

        ChatMessage message = BuildMessage(subSession, conclusion, supersedes: existing >= 0 && !replaceInPlace,
            interruption, othersPending);
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

    /// <summary>
    /// 组装插进派活者进行中那一轮的回信。它醒着说明上一封（若有）已被读过，所以有上一封就写成更正
    /// </summary>
    /// <param name="parent">派活者</param>
    /// <param name="subSession">子会话</param>
    /// <param name="interruption">被打断的原因</param>
    /// <param name="conclusion">回信正文</param>
    /// <param name="othersPending">派活者还在等几位别人的回信</param>
    /// <returns>回信；没有可交的为 null</returns>
    internal static ChatMessage? Compose(ChatSession parent, ChatSession subSession, string? interruption,
        string? conclusion, int othersPending)
    {
        conclusion = string.IsNullOrWhiteSpace(conclusion) ? LastAssistantText(subSession) : conclusion.Trim();
        if (conclusion.Length == 0 && interruption == null) return null;

        bool supersedes = ResolveSlot(parent.History, subSession.SessionId, RequestKey(subSession)).Index >= 0;
        return BuildMessage(subSession, conclusion, supersedes, interruption, othersPending);
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
    /// <param name="requestKey">这封回信回的是哪一次来信（见 <see cref="RequestKey"/>）</param>
    /// <returns>上一份的下标（没有为 -1）与是否原地替换</returns>
    internal static (int Index, bool ReplaceInPlace) ResolveSlot(IReadOnlyList<ChatMessage> parentHistory,
        string subSessionId, string requestKey)
    {
        for (int i = parentHistory.Count - 1; i >= 0; i--)
        {
            if (ChatMessageAnnotations.ReadSubAgentReportSession(parentHistory[i]) != subSessionId) continue;

            // 上一封回的是更早的来信:这次续聊问了新问题,这封是新回答,不占它的位置。
            // 老数据没记来信,按原口径当同一次
            string previousKey = ChatMessageAnnotations.ReadSubAgentReplyTo(parentHistory[i]);
            if (previousKey.Length > 0 && requestKey.Length > 0 && previousKey != requestKey) return (-1, false);

            // 还停在末尾就说明模型没读过它，那一份留着只是垃圾；
            // 后面已经有新轮次的话，它已经被据以回应过，抹掉会让那段回应没有来由
            return (i, i == parentHistory.Count - 1);
        }

        return (-1, false);
    }

    /// <summary>
    /// 写成对方发来的一封信（ADR 0062）：开头说清是谁、回的是你发的哪一句，正文原样跟在后面。
    ///
    /// 不写「委派」「报告」「结论」：对方是你发消息的人，不是一次调用（ADR 0044）。
    /// 用「来自 X」起头而不是群投递的「[名字]: 内容」——user 消息里光有后者，模型会当成用户在说（ADR 0060）。
    /// 署名用与工具回执同一种 <c>[sub-session: …]</c>，照抄进 <c>to</c> 就能接着回
    /// </summary>
    /// <param name="sender">对方的名字或身份，没有为空</param>
    /// <param name="subSessionId">子会话标识</param>
    /// <param name="replyingTo">对方回的是你发的哪一句（取开头）</param>
    /// <param name="conclusion">正文</param>
    /// <param name="supersedes">更正上一封</param>
    /// <param name="interruption">被打断的原因（接在「对方」后面成句），没被打断为 null</param>
    /// <param name="othersPending">还在等几位别人的回信</param>
    /// <returns>信的全文</returns>
    internal static string BuildText(string sender, string subSessionId, string replyingTo, string conclusion,
        bool supersedes, string? interruption, int othersPending)
    {
        // 信头是显示层:给模型短号,存储/槽位判定仍用真实 ID(ChatMessageAnnotations.SubAgentReport)
        string from = sender.Length > 0
            ? $"{sender} [sub-session: {SubSessionIdAlias.Short(subSessionId)}]"
            : $"[sub-session: {SubSessionIdAlias.Short(subSessionId)}]";
        string head = replyingTo.Length > 0 ? $"来自 {from}，回你之前发的「{replyingTo}」" : $"来自 {from}";
        if (supersedes) head += "（更正上一封）";

        // 还在等谁是附言,不插在信头与正文之间:放在最后,也正好是模型读完要决定下一步的位置
        string waiting = othersPending > 0 ? $"\n\n（你还在等 {othersPending} 位的回信。）" : string.Empty;
        if (interruption == null) return $"{head}：\n\n{conclusion}{waiting}";

        return conclusion.Length > 0
            ? $"{head}。对方{interruption}，以下是对方停下前说到的：\n\n{conclusion}{waiting}"
            : $"{head}。对方{interruption}，没回任何内容。{waiting}";
    }

    private static ChatMessage BuildMessage(ChatSession subSession, string conclusion, bool supersedes,
        string? interruption, int othersPending) =>
        Annotate(subSession, BuildText(SenderOf(subSession), subSession.SessionId, ReplyingTo(subSession), conclusion,
            supersedes, interruption, othersPending));

    // 点名的人用派活时那个名字（模型下次 to 填的就是它），匿名的用派活时给的身份
    private static string SenderOf(ChatSession subSession) =>
        subSession.SubAgentName.Length > 0 ? subSession.SubAgentName : subSession.SubAgentRole;

    // 子会话里最后一条有字的用户消息就是派活者发的那句（续聊时是最新那句）。
    // 跑着时插进去的补充不算：它并进了这一轮的回信，回信回的仍是开启这一轮的那句
    private static ChatMessage? LastRequest(ChatSession subSession)
    {
        for (int i = subSession.History.Count - 1; i >= 0; i--)
        {
            ChatMessage message = subSession.History[i];
            if (message.Role != ChatRole.User || string.IsNullOrWhiteSpace(message.Text)) continue;
            if (ChatMessageAnnotations.IsParentInterjection(message)) continue;
            return message;
        }

        return null;
    }

    private static string ReplyingTo(ChatSession subSession)
    {
        string text = LastRequest(subSession)?.Text.Trim() ?? string.Empty;
        // 续聊落进子会话的那句带着给对方看的发信人前缀,引回给派活者时剥掉
        if (text.StartsWith(SubAgentTool.ParentInterjectionPrefix, StringComparison.Ordinal))
            text = text[SubAgentTool.ParentInterjectionPrefix.Length..].TrimStart();
        int lineEnd = text.IndexOf('\n');
        if (lineEnd >= 0) text = text[..lineEnd].TrimEnd();
        return text.Length > QuoteLength ? text[..QuoteLength] + "…" : text;
    }

    /// <summary>回的是哪一次来信：取那条来信的时间戳，消息本身没有稳定编号</summary>
    private static string RequestKey(ChatSession subSession) =>
        LastRequest(subSession)?.CreatedAt?.UtcTicks.ToString() ?? string.Empty;

    /// <summary>盖上后续报告标记：带它的消息要落盘、要供给模型，只是渲染成旁白那一套</summary>
    private static ChatMessage Annotate(ChatSession subSession, string text) =>
        new(ChatRole.User, text)
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatMessageAnnotations.SubAgentReport] = subSession.SessionId,
                [ChatMessageAnnotations.SubAgentReplyTo] = RequestKey(subSession),
            },
        };
}
