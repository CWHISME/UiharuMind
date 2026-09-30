using UiharuMind.Core.AI.Character;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 建群之后改名单：移出、加人、加回（ADR 0046 修订「建群之后增删成员、更换工作区」）。
/// 退群的会话留着（<see cref="ChatSession.HasLeftGroup"/>），加回的是同一份；补多少历史由 <see cref="EGroupBackfill"/> 定。
/// 名单变了，各成员下一轮开跑时按新场景段重建装配（ADR 0048 决策 2）
/// </summary>
public static class GroupMembership
{
    /// <summary>群最少留几位：再移就没有群了</summary>
    public const int MinMembers = 1;

    /// <summary>
    /// 此刻能不能改名单或工作区：群没在跑一波，成员也没在私聊。
    /// 调度会边跑边读名单，而一轮中途被撤要处理投递、审批被他占着，不值得
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <returns>能改为 true</returns>
    public static bool CanEdit(ChatSession group) =>
        !GroupChatCoordinator.Instance.IsRunning(group.SessionId)
        && group.GroupMemberSessionIds.All(id => SessionManager.Instance.Running.StateOf(id) == ESessionRunState.Idle);

    /// <summary>
    /// 本群已退出的成员，按入群先后
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <returns>退群成员的元数据</returns>
    public static IReadOnlyList<ChatSessionMeta> FormerMetasOf(ChatSession group) =>
        SessionManager.Instance.GetGroupMembers(group.SessionId)
            .Where(x => x.HasLeftGroup)
            .OrderBy(x => x.CreatedAt)
            .ToList();

    /// <summary>
    /// 在群里说过话的所有人：名单（发言顺序）在前，退群的在后。
    /// 拆投递、列产物这类回看历史的地方用它——退群前说的话、写的文件还在
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <returns>成员会话标识</returns>
    public static IReadOnlyList<string> EveryMemberIdOf(ChatSession group) =>
        [..group.GroupMemberSessionIds, ..FormerMetasOf(group).Select(x => x.SessionId)];

    /// <summary>
    /// 这个角色在本群有没有退群的会话：有就恢复那一份，同一个角色在一个群里只有一份成员会话
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <param name="characterId">角色标识</param>
    /// <returns>退群成员的元数据；没有为 null</returns>
    public static ChatSessionMeta? FormerOf(ChatSession group, string characterId) =>
        FormerMetasOf(group).FirstOrDefault(x => x.CharacterId == characterId);

    /// <summary>
    /// 移出一位成员：摘出名单、标退群，会话留着。移掉的是主持人就改成无主持人
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <param name="member">成员会话</param>
    /// <param name="load">按标识取会话；null 走会话管理器</param>
    /// <returns>移出了为 true；不在名单里或只剩最后一位为 false</returns>
    public static bool Remove(ChatSession group, ChatSession member, Func<string, ChatSession?>? load = null)
    {
        if (group.GroupMemberSessionIds.Count <= MinMembers) return false;
        if (!group.GroupMemberSessionIds.Remove(member.SessionId)) return false;

        if (group.GroupHostSessionId == member.SessionId) group.GroupHostSessionId = null;
        member.HasLeftGroup = true;
        GroupChatCoordinator.Instance.ForgetMember(member.SessionId);
        SyncDescription(group, load);
        member.SaveMeta(touchUpdatedAt: false);
        group.SaveMeta(touchUpdatedAt: false);
        return true;
    }

    /// <summary>
    /// 加一位新成员：建会话、入库、按补历史的方式定游标，排到名单末尾
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <param name="character">角色（须能进群，见 <see cref="GroupChatSessions.CanJoin"/>）</param>
    /// <param name="modelName">钉选的模型名；null 跟随全局</param>
    /// <param name="backfill">补历史的方式</param>
    /// <returns>新成员会话</returns>
    /// <exception cref="ArgumentException">角色进不了群</exception>
    public static ChatSession Join(ChatSession group, CharacterData character, string? modelName, GroupBackfill backfill)
    {
        if (!GroupChatSessions.CanJoin(character))
            throw new ArgumentException($"'{character.CharacterName}' cannot join a group.", nameof(character));

        ChatSession member = GroupChatSessions.NewMember(group, character, modelName);
        SessionManager.Instance.Add(member);
        Admit(group, member, backfill);
        return member;
    }

    /// <summary>
    /// 把成员（新建的或退群的）放进名单末尾，并按补历史的方式定游标：
    /// 不补 → 流水末尾；全部 → 新人从头、加回的不动；摘要 → 写摘要的人听到哪儿就从哪儿接（加回的取较大者），
    /// 之后的原文随下一次投递照常给。没写成的摘要按不补处理
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <param name="member">成员会话（<see cref="ChatSession.GroupId"/> 已指向本群）</param>
    /// <param name="backfill">补历史的方式</param>
    /// <param name="load">按标识取会话；null 走会话管理器</param>
    public static void Admit(ChatSession group, ChatSession member, GroupBackfill backfill,
        Func<string, ChatSession?>? load = null)
    {
        switch (backfill.Mode)
        {
            case EGroupBackfill.Full:
                break;
            case EGroupBackfill.Summary when backfill.Briefing != null:
                member.GroupCursor = Math.Max(member.GroupCursor, Math.Clamp(backfill.BriefedUpTo, 0, group.History.Count));
                member.GroupBriefing = backfill.Briefing;
                break;
            default:
                member.GroupCursor = group.History.Count;
                break;
        }

        // 插话读过的下标只在游标之后才有意义
        int cursor = member.GroupCursor;
        member.GroupConsumedPosts = [..member.GroupConsumedPosts.Where(x => x >= cursor)];
        member.HasLeftGroup = false;
        if (!group.GroupMemberSessionIds.Contains(member.SessionId)) group.GroupMemberSessionIds.Add(member.SessionId);
        SyncDescription(group, load);
        member.SaveMeta(touchUpdatedAt: false);
        group.SaveMeta(touchUpdatedAt: false);
    }

    /// <summary>
    /// 选全部补发时要交给他多少条群流水（弹窗里写明，一次投递压缩裁不了）
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <param name="former">加回的退群成员；新成员为 null</param>
    /// <returns>条数</returns>
    public static int FullBackfillCount(ChatSession group, ChatSession? former) =>
        Math.Max(0, group.History.Count - (former?.GroupCursor ?? 0));

    /// <summary>
    /// 挑一位写入群摘要：主持人 → 最近在群里发过言的 → 名单里第一位有历史的。都没有为 null
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <param name="load">按标识取会话</param>
    /// <returns>写摘要的成员</returns>
    public static ChatSession? PickBriefingWriter(ChatSession group, Func<string, ChatSession?> load)
    {
        List<ChatSession> candidates = group.GroupMemberSessionIds
            .Select(load)
            .OfType<ChatSession>()
            .Where(x => x.History.Count > 0)
            .ToList();
        if (candidates.FirstOrDefault(x => x.SessionId == group.GroupHostSessionId) is { } host) return host;

        for (int i = group.History.Count - 1; i >= 0; i--)
        {
            string? speaker = ChatMessageAnnotations.GroupSpeakerSessionOf(group.History[i]);
            if (speaker != null && candidates.FirstOrDefault(x => x.SessionId == speaker) is { } recent) return recent;
        }

        return candidates.FirstOrDefault();
    }

    /// <summary>
    /// 请一位成员写入群摘要。他此刻在私聊（闸被占着）就不等，当写不成
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <param name="returning">读者是加回来的退群成员</param>
    /// <param name="cancellationToken">取消标记</param>
    /// <returns>摘要方式的补历史；没人能写或写失败为 null</returns>
    public static async Task<GroupBackfill?> WriteBriefingAsync(ChatSession group, bool returning,
        CancellationToken cancellationToken = default)
    {
        if (PickBriefingWriter(group, id => SessionManager.Instance.Load(id)) is not { } writer) return null;

        using IDisposable? gate = GroupMemberTurnGate.TryEnter(writer.SessionId);
        if (gate == null) return null;

        string? summary = await GroupBackground.WriteBriefingAsync(writer, returning, cancellationToken);
        if (summary == null) return null;

        string writerName = writer.CharacterData.CharacterName;
        return new GroupBackfill(EGroupBackfill.Summary,
            GroupBackground.ComposeBriefing(writerName, summary, returning), writer.GroupCursor, writerName);
    }

    // 群描述就是成员名单（右栏群卡与左栏副标题），名单变了跟着改
    private static void SyncDescription(ChatSession group, Func<string, ChatSession?>? load)
    {
        load ??= id => SessionManager.Instance.Load(id);
        group.Description = string.Join("、", group.GroupMemberSessionIds
            .Select(load)
            .OfType<ChatSession>()
            .Select(x => x.CharacterData.CharacterName));
    }
}

/// <summary>新成员入群或退群成员加回时，补多少群里的历史</summary>
public enum EGroupBackfill
{
    /// <summary>不补：从现在起听</summary>
    None,

    /// <summary>一份摘要，由一位成员来写</summary>
    Summary,

    /// <summary>全部补发：下一轮一次交完</summary>
    Full,
}

/// <summary>补历史的做法</summary>
/// <param name="Mode">方式</param>
/// <param name="Briefing">摘要方式下随下一次投递交出的那一段；其余为 null</param>
/// <param name="BriefedUpTo">摘要覆盖到群流水哪儿（写的人的游标）</param>
/// <param name="WriterName">写摘要的成员名（提示用）</param>
public sealed record GroupBackfill(EGroupBackfill Mode, string? Briefing = null, int BriefedUpTo = 0,
    string? WriterName = null)
{
    /// <summary>不补</summary>
    public static GroupBackfill None { get; } = new(EGroupBackfill.None);

    /// <summary>全部补发</summary>
    public static GroupBackfill Full { get; } = new(EGroupBackfill.Full);
}
