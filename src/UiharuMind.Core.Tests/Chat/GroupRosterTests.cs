using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 「谁在群里」只从 GroupRoster 问：在场认名单，退群认 HasLeftGroup，只读索引的地方凭群壳元数据按发言顺序取。
/// 只比会话与角色标识，不碰角色库（它的重建与别的测试类并行会互相踩）
/// </summary>
public class GroupRosterTests
{
    [Fact]
    public void PresentFollowsTheRoster_FormerFollowsTheFlag()
    {
        ChatSession group = GroupChatSessions.Create("g-roster", false, Cards("a", "b", "c"), null);
        try
        {
            string[] ids = [..group.GroupMemberSessionIds];
            ChatSession b = SessionManager.Instance.Load(ids[1])!;
            Assert.Equal(EGroupRosterEdit.Done, GroupMembership.Remove(group, b));
            // 换过发言顺序：只读索引的地方也要按名单来，不按建会话的先后
            Assert.True(GroupChatCoordinator.Instance.TryEditRoster(group, () =>
            {
                group.GroupMemberSessionIds = [ids[2], ids[0]];
                group.SaveMeta(touchUpdatedAt: false);
            }));

            GroupRoster roster = GroupRoster.Of(group);
            Assert.Equal([ids[2], ids[0]], roster.Present.Select(x => x.SessionId));
            Assert.Equal([ids[1]], roster.Former.Select(x => x.SessionId));
            Assert.Equal([ids[2], ids[0], ids[1]], roster.Everyone.Select(x => x.SessionId));
            Assert.Equal(ids[1], roster.FormerOf("roster-b")?.SessionId);
            Assert.Equal([ids[2], ids[0]],
                GroupRoster.Of(SessionManager.Instance.GetMeta(group.SessionId)!).Present.Select(x => x.SessionId));
        }
        finally
        {
            SessionManager.Instance.Delete(group.SessionId);
        }
    }

    /// <summary>旧索引的群壳元数据还没带名单：退回按入群先后、排除退群的人</summary>
    [Fact]
    public void MetaWithoutRoster_FallsBackToCreationOrder()
    {
        ChatSession group = GroupChatSessions.Create("g-roster-old", false, Cards("a", "b"), null);
        try
        {
            ChatSessionMeta meta = SessionManager.Instance.GetMeta(group.SessionId)!;
            meta.GroupMemberSessionIds = [];

            Assert.Equal(["roster-a", "roster-b"], GroupRoster.Of(meta).Present.Select(x => x.Meta.CharacterId));
        }
        finally
        {
            SessionManager.Instance.Delete(group.SessionId);
        }
    }

    private static List<CharacterData> Cards(params string[] names) =>
        names.Select(x => new CharacterData { CharacterId = "roster-" + x, CharacterName = x }).ToList();
}
