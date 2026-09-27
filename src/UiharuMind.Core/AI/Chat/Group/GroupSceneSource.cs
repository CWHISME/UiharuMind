using UiharuMind.Core.AI.Character;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 成员会话 → 群场景段正文（ADR 0048）。装配经 <c>AgentBuildProfile.FromSession</c> 来这里取，
/// 快照与装配读的是同一份，所以群名、名单、主持人一变，下一次挂接就重建
/// </summary>
public static class GroupSceneSource
{
    /// <summary>
    /// 取成员会话的群场景段正文（读会话管理器与角色管理器）
    /// </summary>
    /// <param name="member">会话</param>
    /// <returns>场景段正文；不是群成员或群已不在为空串</returns>
    public static string For(ChatSession member)
    {
        if (!member.IsGroupMember) return string.Empty;
        return For(member, SessionManager.Instance.Load(member.GroupId!),
            MemberNameOf, CharacterManager.Instance.UserCharacterName);
    }

    /// <summary>按成员会话标识取显示名；取不到为 null（那位不列）。与右栏成员列表同一份名单口径</summary>
    private static string? MemberNameOf(string id) =>
        SessionManager.Instance.GetMeta(id) is { } meta ? SessionManager.CharacterOf(meta).CharacterName : null;

    /// <summary>
    /// 取成员会话的群场景段正文（显式入参，可单测）
    /// </summary>
    /// <param name="member">成员会话</param>
    /// <param name="group">他所在的群壳；为 null 视为群已不在</param>
    /// <param name="nameOf">按成员会话标识取显示名；取不到为 null（那位不列）</param>
    /// <param name="userName">用户的名字</param>
    /// <returns>场景段正文；不是这个群的成员为空串</returns>
    public static string For(ChatSession member, ChatSession? group, Func<string, string?> nameOf, string userName)
    {
        if (group is not { IsGroup: true } || member.GroupId != group.SessionId) return string.Empty;

        List<string> others = group.GroupMemberSessionIds
            .Where(x => x != member.SessionId)
            .Select(nameOf)
            .OfType<string>()
            .ToList();
        CharacterData self = member.CharacterData;
        string? hostName = group.GroupHostSessionId == member.SessionId
            ? self.CharacterName
            : group.GroupHostSessionId is { } hostId ? nameOf(hostId) : null;
        // 有没有 SendMessage 可用看会话形态而非卡身份（ADR 0050）：chat 形态的 agent 卡不装工具
        bool canPostMidTurn = member.IsAgentForm is true && self.Tools.EnableSubAgent;
        return GroupTranscript.BuildScene(new GroupScene(group.Title, self.CharacterName, others, userName,
            canPostMidTurn, hostName));
    }
}
