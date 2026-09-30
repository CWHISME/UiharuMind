using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Features.Conversation;
using UiharuMind.App.Tests.TestDoubles;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 切会话时页面先调 <see cref="ConversationViewModel.LoadSessionAsync"/>、再换绑实例，绑定在装载的<b>同步段</b>之后
/// 立刻求值。群与群成员的形态判据必须在这一刻就对——晚一步，右栏就先按单聊画出群壳的占位角色再跳成群卡
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ConversationGroupLoadTests
{
    [Fact]
    public void LoadingGroupShell_IsGroupBeforeFirstAwait()
    {
        HeadlessUi.Run(() =>
        {
            ChatSession group = CreateGroup();
            try
            {
                using ConversationViewModel vm = new(new RecordingMessageService());
                _ = vm.LoadSessionAsync(SessionManager.Instance.GetMeta(group.SessionId));

                Assert.True(vm.IsGroupSession);
                Assert.Equal(group.Title, vm.Group?.Title);
            }
            finally
            {
                SessionManager.Instance.Delete(group.SessionId);
            }
        });
    }

    [Fact]
    public void LoadingGroupMember_IsMemberBeforeFirstAwait()
    {
        HeadlessUi.Run(() =>
        {
            ChatSession group = CreateGroup();
            try
            {
                using ConversationViewModel vm = new(new RecordingMessageService());
                _ = vm.LoadSessionAsync(SessionManager.Instance.GetGroupMembers(group.SessionId)[0]);

                Assert.True(vm.IsGroupMemberSession);
                Assert.False(vm.IsPermissionEditable); //成员跟群走，不在这里改
                Assert.False(vm.IsGroupSession);
            }
            finally
            {
                SessionManager.Instance.Delete(group.SessionId);
            }
        });
    }

    private static ChatSession CreateGroup() => GroupChatSessions.Create("g-load", false,
        [new CharacterData { CharacterName = "A" }, new CharacterData { CharacterName = "B" }], null, null);
}
