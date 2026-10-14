using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Chat.Group.Away;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 无限模式心跳日志（ADR 0055 §8）：心跳行不进群，逐条落草稿目录的心跳.md（留给用户与巡检），
/// 产物指纹排除它（GroupArtifacts）
/// </summary>
public class GroupHeartbeatLogTests
{
    public GroupHeartbeatLogTests()
    {
        DefaultCharacterManager.Instance.OnInitialize();
    }

    [Fact]
    public void Append_WritesHeaderAndEachHeartbeatLineWithTimestamp()
    {
        ChatSession group = GroupChatSessions.Create("心跳测试", true, Cards("a", "b"), "/tmp/hb-test-ws");
        try
        {
            string path = GroupHeartbeatLog.PathOf(group);
            if (File.Exists(path)) File.Delete(path);

            GroupHeartbeatLog.Append(group, "⟦运行中｜锚点：x｜delta：有⟧");
            GroupHeartbeatLog.Append(group, "⟦运行中｜锚点：y｜delta：无⟧");

            string text = File.ReadAllText(path);
            Assert.StartsWith("# 心跳测试 心跳", text);
            Assert.Contains("⟦运行中｜锚点：x｜delta：有⟧", text);
            Assert.Contains("⟦运行中｜锚点：y｜delta：无⟧", text);
            Assert.Contains("[", text); // 时间戳前缀
        }
        finally
        {
            SessionManager.Instance.Delete(group.SessionId);
        }
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