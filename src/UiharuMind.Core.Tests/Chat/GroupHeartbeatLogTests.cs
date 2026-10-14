using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Chat.Group.Away;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 无限模式心跳文件（ADR 0055 §8）：化身每轮自己用 Write/Edit 写进草稿目录的心跳.md（留给用户与巡检），
/// 产物指纹排除它（GroupArtifacts）
/// </summary>
public class GroupHeartbeatLogTests
{
    public GroupHeartbeatLogTests()
    {
        DefaultCharacterManager.Instance.OnInitialize();
    }

    [Fact]
    public void PathOf_LandsInTheGroupDraftRoom()
    {
        ChatSession group = GroupChatSessions.Create("心跳测试路径", true, Cards("a", "b"), "/tmp/hb-test-ws2");
        try
        {
            Assert.Equal(
                Path.Combine(GroupArtifacts.DraftRoomOf(group), GroupHeartbeatLog.FileName),
                GroupHeartbeatLog.PathOf(group));
        }
        finally
        {
            SessionManager.Instance.Delete(group.SessionId);
        }
    }

    private static IReadOnlyList<CharacterData> Cards(params string[] ids) =>
        ids.Select(x => new CharacterData { CharacterId = $"avatar-{x}", CharacterName = x }).ToList();
}
