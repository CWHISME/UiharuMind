using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 群壳清空历史(<see cref="ChatSession.Clear"/>)必须把成员与化身的投递游标归零:
/// 游标是群流水下标,历史一清就失效——不归零的话新发言永远落在游标之前,
/// <c>BuildDelivery</c> 恒空,整个群再也听不到新话(清一次历史就把群聊弄聋)。
/// </summary>
public class ChatSessionClearGroupTests
{
    [Fact]
    public void Clear_ResetsMemberAndAvatarCursors()
    {
        DefaultCharacterManager.Instance.OnInitialize();
        ChatSession group = GroupChatSessions.Create("会审", isAgentGroup: true,
            [Character("Alice"), Character("Bob")], workspacePath: "/tmp/ws");
        try
        {
            ChatSession alice = SessionManager.Instance.Load(group.GroupMemberSessionIds[0])!;
            ChatSession bob = SessionManager.Instance.Load(group.GroupMemberSessionIds[1])!;
            ChatSession avatar = GroupAvatar.EnsureFor(group, null);
            // 旧流水都听过了:游标在末尾,还插话读过一条
            alice.GroupCursor = 5;
            bob.GroupCursor = 5;
            avatar.GroupCursor = 5;
            alice.GroupConsumedPosts = [2];
            bob.GroupConsumedPosts = [2];
            avatar.GroupConsumedPosts = [2];
            alice.SaveMeta(touchUpdatedAt: false);
            bob.SaveMeta(touchUpdatedAt: false);
            avatar.SaveMeta(touchUpdatedAt: false);
            group.History.Add(new ChatMessage(ChatRole.User, "旧发言"));

            group.Clear();

            Assert.Empty(group.History);
            Assert.Equal(0, alice.GroupCursor);
            Assert.Equal(0, bob.GroupCursor);
            Assert.Equal(0, avatar.GroupCursor);
            Assert.Empty(alice.GroupConsumedPosts);
            Assert.Empty(bob.GroupConsumedPosts);
            Assert.Empty(avatar.GroupConsumedPosts);
        }
        finally
        {
            foreach (string id in group.GroupMemberSessionIds) SessionManager.Instance.Delete(id);
            if (GroupAvatar.MetaOf(group.SessionId) is { } avatarMeta) SessionManager.Instance.Delete(avatarMeta.SessionId);
            SessionManager.Instance.Delete(group.SessionId);
        }
    }

    /// <summary>清完历史之后新发言要能送到成员手里:「游标归零」是「清空后群还活着」的直接判据</summary>
    [Fact]
    public async Task Clear_ThenPost_DeliversNewMessagesToMembers()
    {
        DefaultCharacterManager.Instance.OnInitialize();
        ChatSession group = GroupChatSessions.Create("会审", isAgentGroup: false,
            [Character("Alice"), Character("Bob")], workspacePath: null);
        try
        {
            ChatSession alice = SessionManager.Instance.Load(group.GroupMemberSessionIds[0])!;
            ChatSession bob = SessionManager.Instance.Load(group.GroupMemberSessionIds[1])!;
            alice.GroupCursor = 5; //清空前的游标:若不归零,下标 0 的新发言会被它跳过去
            bob.GroupCursor = 5;
            alice.SaveMeta(touchUpdatedAt: false);
            bob.SaveMeta(touchUpdatedAt: false);

            group.Clear();

            FakeGroupMemberTurnRunner runner = new();
            GroupChatCoordinator coordinator = new(runner, id => SessionManager.Instance.Load(id));
            await coordinator.PostAsync(group, "大家好");

            Assert.NotEmpty(runner.Calls);
            Assert.All(runner.Calls, call => Assert.Contains("大家好", call.Input));
        }
        finally
        {
            foreach (string id in group.GroupMemberSessionIds) SessionManager.Instance.Delete(id);
            SessionManager.Instance.Delete(group.SessionId);
        }
    }

    private static CharacterData Character(string name) => new() { CharacterName = name };
}
