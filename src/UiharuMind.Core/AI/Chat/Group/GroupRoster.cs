using UiharuMind.Core.AI.Character;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 一个群里的人：在场（名单，发言顺序）、已退出（按入群先后）与全部。
/// 「谁在群里」只从这里问——右栏成员列表、场景段、@ 与唤醒、投递渲染、产物区、左栏头像拼图共用这一份口径，
/// 在场只认群壳上的名单，退群只认成员会话上的 <see cref="ChatSession.HasLeftGroup"/>。
/// 是一张快照：名单改了（<see cref="GroupMembership.RosterChanged"/>）重新取
/// </summary>
public sealed class GroupRoster
{
    private GroupRoster(IReadOnlyList<GroupRosterMember> present, IReadOnlyList<GroupRosterMember> former)
    {
        Present = present;
        Former = former;
    }

    /// <summary>在场的成员，发言顺序；已删的成员会话跳过</summary>
    public IReadOnlyList<GroupRosterMember> Present { get; }

    /// <summary>已退出的成员，按入群先后</summary>
    public IReadOnlyList<GroupRosterMember> Former { get; }

    /// <summary>在群里待过的所有人（在场在前）：回看历史的地方用——退群前说的话、写的文件还在</summary>
    public IEnumerable<GroupRosterMember> Everyone => Present.Concat(Former);

    /// <summary>
    /// 这个角色在本群有没有退群的会话：有就恢复那一份，同一个角色在一个群里只有一份成员会话
    /// </summary>
    /// <param name="characterId">角色标识</param>
    /// <returns>退群成员；没有为 null</returns>
    public GroupRosterMember? FormerOf(string characterId) =>
        Former.FirstOrDefault(x => x.Meta.CharacterId == characterId);

    /// <summary>
    /// 取群的名单（读会话管理器的索引，不加载成员本体）
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <returns>名单快照；不是群壳为空</returns>
    public static GroupRoster Of(ChatSession group) =>
        group.IsGroup ? FromIndex(group.SessionId, group.GroupMemberSessionIds) : Empty;

    /// <summary>
    /// 只凭群壳的元数据取名单（左栏这类只读索引的地方用，不必加载群壳）
    /// </summary>
    /// <param name="group">群壳会话的元数据</param>
    /// <returns>名单快照；不是群壳为空</returns>
    public static GroupRoster Of(ChatSessionMeta group)
    {
        if (!group.IsGroup) return Empty;
        // 旧索引还没有这一项（群壳下次落盘时补上）：退回按入群先后近似发言顺序（建群时逐个建出）
        IReadOnlyList<string> ids = group.GroupMemberSessionIds.Count > 0
            ? group.GroupMemberSessionIds
            : SessionManager.Instance.GetGroupMembers(group.SessionId)
                .Where(x => !x.HasLeftGroup)
                .OrderBy(x => x.CreatedAt)
                .Select(x => x.SessionId)
                .ToList();
        return FromIndex(group.SessionId, ids);
    }

    /// <summary>
    /// 按注入的取会话函数取在场名单（调度器与它的测试用：成员是临时会话、不在索引里）。不含退群的人
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <param name="load">按标识取会话</param>
    /// <returns>名单快照</returns>
    public static GroupRoster Of(ChatSession group, Func<string, ChatSession?> load) =>
        new([..group.GroupMemberSessionIds
            .Select(load)
            .OfType<ChatSession>()
            .Select(x => new GroupRosterMember(x.CharacterData, x.ToMeta()))], []);

    private static GroupRoster Empty { get; } = new([], []);

    private static GroupRoster FromIndex(string groupId, IReadOnlyList<string> memberIds)
    {
        List<GroupRosterMember> present = memberIds
            .Select(id => SessionManager.Instance.GetMeta(id))
            .OfType<ChatSessionMeta>()
            .Select(Member)
            .ToList();
        List<GroupRosterMember> former = SessionManager.Instance.GetGroupMembers(groupId)
            .Where(x => x.HasLeftGroup)
            .OrderBy(x => x.CreatedAt)
            .Select(Member)
            .ToList();
        return new GroupRoster(present, former);

        static GroupRosterMember Member(ChatSessionMeta meta) => new(SessionManager.CharacterOf(meta), meta);
    }
}

/// <summary>名单里的一位</summary>
/// <param name="Character">他的角色卡</param>
/// <param name="Meta">他的成员会话的元数据</param>
public sealed record GroupRosterMember(CharacterData Character, ChatSessionMeta Meta)
{
    /// <summary>成员会话标识</summary>
    public string SessionId => Meta.SessionId;

    /// <summary>显示名</summary>
    public string Name => Character.CharacterName;
}
