using System.Text;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 群流水与成员会话之间的换算（ADR 0046）。全是纯函数：
/// <see cref="GroupChatCoordinator"/> 决定「什么时候、交给谁」，这里只回答「交什么、算什么」。
/// </summary>
public static class GroupTranscript
{
    /// <summary>
    /// 把游标之后、这个成员还没听过的群发言合成<b>一条</b>投递正文，逐条标发言人。
    ///
    /// 合成一条是为了守住 user / assistant 严格交替——部分本地对话模板遇到连续的 user 消息会直接报错。
    /// 跳过两类：他自己的发言（本来就在他自己的会话里），以及已经插话插给他的。
    /// </summary>
    /// <param name="groupLog">群流水</param>
    /// <param name="cursor">这个成员的游标：之前的都已交给过他</param>
    /// <param name="memberSessionId">成员会话标识</param>
    /// <param name="injected">已经插话插给他的群流水下标；没有为 null</param>
    /// <returns>投递正文；没有新话可交为 null</returns>
    public static string? BuildDelivery(IReadOnlyList<ChatMessage> groupLog, int cursor, string memberSessionId,
        IReadOnlySet<int>? injected = null)
    {
        StringBuilder text = new();
        for (int i = Math.Max(0, cursor); i < groupLog.Count; i++)
        {
            ChatMessage post = groupLog[i];
            if (ChatMessageAnnotations.GroupSpeakerSessionOf(post) == memberSessionId) continue;
            if (injected?.Contains(i) == true) continue;

            string body = post.Text.Trim();
            if (body.Length == 0) continue;

            if (text.Length > 0) text.Append("\n\n");
            text.Append(FormatPost(post.AuthorName, body));
        }

        return text.Length == 0 ? null : text.ToString();
    }

    /// <summary>
    /// 一条群发言交给别人时的样子。结构化地知道是谁说的（是谁的那一轮），
    /// 所以只在<b>交给模型</b>时拼成前缀，不从正文里反解析（方案 §6.5 的坑）。
    /// </summary>
    /// <param name="speaker">发言人显示名</param>
    /// <param name="body">正文</param>
    /// <returns>带发言人前缀的一段</returns>
    public static string FormatPost(string? speaker, string body) =>
        $"[{(string.IsNullOrWhiteSpace(speaker) ? "?" : speaker)}]: {body}";

    /// <summary>
    /// 场景说明，只随第一次投递给出。放在消息里而不是系统提示：成员会话建好就定下的事，
    /// 放在历史开头同样一次定好，还不用为群去动装配（系统提示与工具定义一字不变，前缀稳定）。
    /// </summary>
    /// <param name="groupName">群名</param>
    /// <param name="selfName">这个成员的名字</param>
    /// <param name="otherNames">其余成员的名字</param>
    /// <param name="userName">用户的名字</param>
    /// <param name="canPostMidTurn">他有没有 SendMessage 可用（智能体且开着委派）</param>
    /// <returns>场景说明</returns>
    public static string BuildScene(string groupName, string selfName, IReadOnlyList<string> otherNames,
        string userName, bool canPostMidTurn)
    {
        string others = otherNames.Count == 0 ? "" : "、" + string.Join("、", otherNames);
        string midTurn = canPostMidTurn
            ? "想在这一轮中途先对大家说一句，可以调用 SendMessage，to 写 group；用过它，这一轮最后的正文就只留在你这里，不再贴到群里。"
            : "";
        return $"（这是群聊「{groupName}」。在场的有：{userName}（用户）{others}。你是{selfName}。" +
               "群里的发言会按「[名字]: 内容」的格式交给你；你这一轮最后的回复正文，就是你在群里说的话——" +
               $"直接说，不要自己加「[名字]:」前缀。{midTurn}）";
    }

    /// <summary>
    /// 这一轮里成员最后说的那段正文。他没用 SendMessage 发群时，这段就是他的群发言（ADR 0046 决策 4）。
    /// </summary>
    /// <param name="memberHistory">成员会话的历史</param>
    /// <param name="fromIndex">这一轮开始前的历史长度</param>
    /// <returns>正文；这一轮一个字都没说为 null</returns>
    public static string? PickFinalText(IReadOnlyList<ChatMessage> memberHistory, int fromIndex)
    {
        for (int i = memberHistory.Count - 1; i >= Math.Max(0, fromIndex); i--)
        {
            ChatMessage message = memberHistory[i];
            if (message.Role != ChatRole.Assistant) continue;

            string text = message.Text.Trim();
            if (text.Length > 0) return text;
        }

        return null;
    }

    /// <summary>从某个下标起，这个成员有没有自己往群里发过话</summary>
    /// <param name="groupLog">群流水</param>
    /// <param name="fromIndex">起点下标</param>
    /// <param name="memberSessionId">成员会话标识</param>
    /// <returns>发过为 true</returns>
    public static bool PostedSince(IReadOnlyList<ChatMessage> groupLog, int fromIndex, string memberSessionId)
    {
        for (int i = Math.Max(0, fromIndex); i < groupLog.Count; i++)
        {
            if (ChatMessageAnnotations.GroupSpeakerSessionOf(groupLog[i]) == memberSessionId) return true;
        }

        return false;
    }
}
