using UiharuMind.Core.AI.Character;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 建群：一个群壳会话 + 每个成员一个真会话（ADR 0046 决策 1、2）。
/// 谁能进哪类群也只在这里定义一次，建群弹窗的候选与这里的校验同源。
/// </summary>
public static class GroupChatSessions
{
    /// <summary>
    /// 群把权限档推给了成员（参数为群壳会话标识）。打开着的成员窗口据此刷新显示的档位——
    /// 它只在打开时读一次，不喊一声就一直显示旧档。⚠️ 可能来自任意线程，订阅方自行 marshal
    /// </summary>
    public static event Action<string>? PermissionApplied;

    /// <summary>
    /// 这个角色能不能进这类群：两类群都收<b>所有非用户卡</b>（ADR 0050 决策 3）。
    /// 智能体群绑工作区，agent 成员跑 agent 形态、普通成员跑 chat 形态；
    /// 普通群不绑工作区，全员以 chat 形态加入——agent 卡进普通群也不写文件，
    /// ADR 0042「普通群只收普通角色」的排除理由在 chat 形态下失效。
    /// 群类型只影响加入后的形态、不影响能不能进，所以这里不接群类型。
    /// </summary>
    /// <param name="character">角色</param>
    /// <returns>能进为 true</returns>
    public static bool CanJoin(CharacterData character) =>
        character.CanStartSession();

    /// <summary>
    /// 建一个群并落盘。成员会话先入库、群壳最后入库：列表见到群的那一刻，成员已经齐了
    /// </summary>
    /// <param name="name">群名</param>
    /// <param name="isAgentGroup">是不是智能体群（建群时定、之后不变）</param>
    /// <param name="members">成员，顺序即发言顺序</param>
    /// <param name="workspacePath">智能体群的工作区；普通群忽略</param>
    /// <param name="memberModelNames">成员各自的模型名，顺序与 <paramref name="members"/> 一致；null = 该成员跟随全局</param>
    /// <param name="schedule">调度设置；null 为串行、无主持人</param>
    /// <returns>群壳会话</returns>
    /// <exception cref="ArgumentException">没有成员，或有成员进不了这类群</exception>
    public static ChatSession Create(string name, bool isAgentGroup, IReadOnlyList<CharacterData> members,
        string? workspacePath, IReadOnlyList<string?>? memberModelNames = null, GroupSchedule? schedule = null)
    {
        if (members.Count == 0) throw new ArgumentException("A group needs at least one member.", nameof(members));
        if (members.FirstOrDefault(x => !CanJoin(x)) is { } refused)
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
            GroupScheduleMode = schedule?.Mode ?? EGroupScheduleMode.Serial,
            GroupStopPolicy = schedule?.StopPolicy ?? EGroupStopPolicy.Conservative,
        };

        // 建群时按成员逐个钉选模型；没给就当跟随全局
        List<ChatSession> sessions = members
            .Select((character, i) => NewMember(group, character,
                memberModelNames != null && i < memberModelNames.Count ? memberModelNames[i] : null))
            .ToList();
        group.GroupMemberSessionIds = sessions.Select(x => x.SessionId).ToList();
        if (schedule?.HostIndex is { } host && host >= 0 && host < sessions.Count)
        {
            group.GroupHostSessionId = sessions[host].SessionId;
        }

        foreach (ChatSession member in sessions) SessionManager.Instance.Add(member);
        SessionManager.Instance.Add(group);
        return group;
    }

    /// <summary>
    /// 为群建一个成员会话（不入库、不进名单）。建群与之后加人共用
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <param name="character">成员角色</param>
    /// <param name="modelName">钉选的模型名；null 跟随全局</param>
    /// <returns>成员会话</returns>
    public static ChatSession NewMember(ChatSession group, CharacterData character, string? modelName) =>
        // 不走带角色的构造：那会写入开场白，而在群里开场白是他对着空气自我介绍
        new()
        {
            CharacterId = character.CharacterId,
            Title = $"{group.Title} · {character.CharacterName}",
            Description = group.Title,
            GroupId = group.SessionId,
            // 成员形态由群类型 × 成员身份决定，不由成员自由选（ADR 0050 决策 3）：
            // 智能体群的 agent 成员跑 agent 形态；普通群全员 chat 形态（agent 卡也不带工具）
            IsAgentForm = group.IsAgentGroup && character.IsAgent,
            WorkspacePath = character.IsAgent ? group.WorkspacePath : null,
            SessionModelName = modelName,
        };

    /// <summary>
    /// 成员的有效权限档：一律取群的。群聊的权限只在群视图一处设，成员窗口里不各设一份——
    /// 各存一份时，用户看着群是「自动编辑」，某位成员却在完全自动档下跑 shell
    /// </summary>
    /// <param name="member">会话</param>
    /// <returns>权限档序号；不是群成员或群已不在时取他自己的</returns>
    public static int PermissionOf(ChatSession member) =>
        member.GroupId is { } groupId && SessionManager.Instance.GetMeta(groupId) is { } group
            ? group.PermissionModeIndex
            : member.PermissionModeIndex;

    /// <summary>
    /// 群改了权限档：当场推给各成员。审批规则现取档位，正在跑的那一轮下一条调用就按新档来
    /// </summary>
    /// <param name="group">群壳会话</param>
    public static void ApplyPermissionToMembers(ChatSession group)
    {
        if (!group.IsGroup) return;
        foreach (string memberId in group.GroupMemberSessionIds)
        {
            if (SessionManager.Instance.Load(memberId) is not { } member) continue;
            if (member.PermissionModeIndex == group.PermissionModeIndex) continue;
            member.PermissionModeIndex = group.PermissionModeIndex;
            member.SaveMeta(touchUpdatedAt: false);
        }

        PermissionApplied?.Invoke(group.SessionId);
    }

    /// <summary>
    /// 群换了工作区：当场推给 agent 形态的成员。开跑前本来也会对齐，这里是让成员窗口与右栏预演立刻对上
    /// </summary>
    /// <param name="group">群壳会话</param>
    public static void ApplyWorkspaceToMembers(ChatSession group)
    {
        if (!group.IsGroup) return;
        foreach (string memberId in group.GroupMemberSessionIds)
        {
            if (SessionManager.Instance.Load(memberId) is not { IsAgentForm: true } member) continue;
            if (member.WorkspacePath == group.WorkspacePath) continue;
            member.WorkspacePath = group.WorkspacePath;
            member.SaveMeta(touchUpdatedAt: false);
        }
    }
}

/// <summary>建群时的调度设置（ADR 0049）</summary>
/// <param name="Mode">调度模式</param>
/// <param name="StopPolicy">并行的停止条件</param>
/// <param name="HostIndex">主持人在成员里的下标；-1 为无主持人</param>
public sealed record GroupSchedule(EGroupScheduleMode Mode, EGroupStopPolicy StopPolicy, int HostIndex);
