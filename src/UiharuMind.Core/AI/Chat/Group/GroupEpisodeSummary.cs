using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 一波收场时的回顾（ADR 0055：离席靠它在波末叫醒化身）。
/// 只带调度宿主自己知道的事；这一波里谁说了什么，按 <see cref="Start"/> / <see cref="End"/> 回群流水去看
/// </summary>
/// <param name="Group">群壳会话</param>
/// <param name="KickoffPostIndex">开这一波的那句用户发言在群流水里的下标；「继续」开的为 null</param>
/// <param name="Start">开波时的群流水长度（开波那句用户发言也算在这一波里）</param>
/// <param name="End">收场时的群流水长度</param>
/// <param name="Stopped">被用户停下</param>
public sealed record GroupEpisodeSummary(ChatSession Group, int? KickoffPostIndex, int Start, int End, bool Stopped)
{
    /// <summary>这一波里成员说了几句（用户与化身的不算）</summary>
    public int MemberPostCount =>
        Group.History.Skip(Start).Take(End - Start).Count(IsMemberPost);

    private static bool IsMemberPost(ChatMessage message) =>
        ChatMessageAnnotations.GroupSpeakerSessionOf(message) != null;
}
