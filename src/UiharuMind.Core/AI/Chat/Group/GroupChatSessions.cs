using UiharuMind.Core.AI.Character;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 建群：一个群壳会话 + 每个成员一个真会话（ADR 0046 决策 1、2）。
/// 谁能进哪类群也只在这里定义一次，建群弹窗的候选与这里的校验同源。
/// </summary>
public static class GroupChatSessions
{
    /// <summary>
    /// 这个角色能不能进这类群：智能体群两类都收，普通群只收普通角色（ADR 0042「已定」）。
    /// 普通群不绑工作区，智能体进去了写的文件没处落
    /// </summary>
    /// <param name="character">角色</param>
    /// <param name="isAgentGroup">是不是智能体群</param>
    /// <returns>能进为 true</returns>
    public static bool CanJoin(CharacterData character, bool isAgentGroup) =>
        character.CanStartSession() && (isAgentGroup || character.IsChat());

    /// <summary>
    /// 建一个群并落盘。成员会话先入库、群壳最后入库：列表见到群的那一刻，成员已经齐了
    /// </summary>
    /// <param name="name">群名</param>
    /// <param name="isAgentGroup">是不是智能体群（建群时定、之后不变）</param>
    /// <param name="members">成员，顺序即发言顺序</param>
    /// <param name="workspacePath">智能体群的工作区；普通群忽略</param>
    /// <returns>群壳会话</returns>
    /// <exception cref="ArgumentException">没有成员，或有成员进不了这类群</exception>
    public static ChatSession Create(string name, bool isAgentGroup, IReadOnlyList<CharacterData> members,
        string? workspacePath)
    {
        if (members.Count == 0) throw new ArgumentException("A group needs at least one member.", nameof(members));
        if (members.FirstOrDefault(x => !CanJoin(x, isAgentGroup)) is { } refused)
        {
            throw new ArgumentException($"'{refused.CharacterName}' cannot join this kind of group.", nameof(members));
        }

        string? groupWorkspace = isAgentGroup ? workspacePath : null;
        ChatSession group = new()
        {
            Title = name,
            Description = string.Join("、", members.Select(x => x.CharacterName)),
            IsGroup = true,
            IsAgentGroup = isAgentGroup,
            WorkspacePath = groupWorkspace,
        };

        // 不走带角色的构造：那会写入开场白，而在群里开场白是他对着空气自我介绍
        List<ChatSession> sessions = members
            .Select(character => new ChatSession
            {
                CharacterId = character.CharacterId,
                Title = $"{name} · {character.CharacterName}",
                Description = name,
                GroupId = group.SessionId,
                WorkspacePath = character.IsAgent ? groupWorkspace : null,
            })
            .ToList();
        group.GroupMemberSessionIds = sessions.Select(x => x.SessionId).ToList();

        foreach (ChatSession member in sessions) SessionManager.Instance.Add(member);
        SessionManager.Instance.Add(group);
        return group;
    }
}
