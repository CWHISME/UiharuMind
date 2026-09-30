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
            MemberCharacterOf, CharacterManager.Instance.UserCharacterName);
    }

    /// <summary>按成员会话标识取角色卡；取不到为 null（那位不列）。与右栏成员列表同一份名单口径</summary>
    private static CharacterData? MemberCharacterOf(string id) =>
        SessionManager.Instance.GetMeta(id) is { } meta ? SessionManager.CharacterOf(meta) : null;

    /// <summary>
    /// 取成员会话的群场景段正文（显式入参，可单测）
    /// </summary>
    /// <param name="member">成员会话</param>
    /// <param name="group">他所在的群壳；为 null 视为群已不在</param>
    /// <param name="characterOf">按成员会话标识取角色卡；取不到为 null（那位不列）</param>
    /// <param name="userName">用户的名字</param>
    /// <returns>场景段正文；不是这个群的成员（含已退群的）为空串</returns>
    public static string For(ChatSession member, ChatSession? group, Func<string, CharacterData?> characterOf, string userName)
    {
        if (group is not { IsGroup: true } || member.GroupId != group.SessionId) return string.Empty;
        // 退群的人 GroupId 还在，只认名单：他之后私聊就是个普通角色，不再挂发群工具
        if (!group.GroupMemberSessionIds.Contains(member.SessionId)) return string.Empty;

        List<GroupMemberPresence> others = group.GroupMemberSessionIds
            .Where(x => x != member.SessionId)
            .Select(characterOf)
            .OfType<CharacterData>()
            .Where(x => !string.IsNullOrWhiteSpace(x.CharacterName))
            .Select(x => new GroupMemberPresence(x.CharacterName, x.Works))
            .ToList();
        CharacterData self = member.CharacterData;
        string? hostName = group.GroupHostSessionId == member.SessionId
            ? self.CharacterName
            : group.GroupHostSessionId is { } hostId ? characterOf(hostId)?.CharacterName : null;
        // 群发言工具随 agent 形态必挂（AgentAssembler），看会话形态而非卡身份（ADR 0050）：chat 形态的 agent 卡不装工具
        bool agentForm = member.IsAgentForm is true;
        // 与装配给不给草稿目录段同一判据（AgentAssemblyFacts.OutputFolderName）
        bool sharesDraftRoom = agentForm && (self.Tools.EnableFileAccess || self.Tools.EnableShellExecution);
        return GroupTranscript.BuildScene(new GroupScene(group.Title, self.CharacterName, others, userName,
            agentForm, hostName, sharesDraftRoom));
    }
}
