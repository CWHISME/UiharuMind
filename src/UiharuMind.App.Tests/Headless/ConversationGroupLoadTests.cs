using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Features.Conversation;
using UiharuMind.App.Tests.TestDoubles;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 切会话时页面先调 <see cref="ConversationViewModel.LoadSessionAsync"/>、再换绑实例，绑定在装载完成时求值。
/// 群与群成员的形态判据必须在这一刻就对——晚一步，右栏就先按单聊画出群壳的占位角色再跳成群卡。
/// load-session 已挪后台线程（主题六 switch 异步化），Group 在装载完成（await 后）建立；
/// 右栏已后置物化（主题六 B），Group 就位前右栏是空壳、不画占位角色，窗口被吸收。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ConversationGroupLoadTests
{
    [Fact]
    public void LoadingGroupShell_EstablishesGroupByLoadCompletion()
    {
        HeadlessUi.RunAsync(async () =>
        {
            ChatSession group = CreateGroup();
            try
            {
                using ConversationViewModel vm = new(new RecordingMessageService());
                await vm.LoadSessionAsync(SessionManager.Instance.GetMeta(group.SessionId));

                Assert.True(vm.IsGroupSession);
                Assert.Equal(group.Title, vm.Group?.Title);
            }
            finally
            {
                SessionManager.Instance.Delete(group.SessionId);
            }
        });
    }

    /// <summary>
    /// 加载中切走（主题六 switch 异步化的作废路径）：第一次装载还在后台读盘时第二次装载进来，
    /// 旧加载必须在悬挂点自行放弃，新会话不串旧内容。loadVersion 机制在 Task.Run await 后仍生效
    /// </summary>
    [Fact]
    public void SwitchingToAnotherSession_WhileLoading_AbandonsOlderLoad()
    {
        HeadlessUi.RunAsync(async () =>
        {
            ChatSession a = CreateGroup("a-load");
            ChatSession b = CreateGroup("b-load");
            try
            {
                using ConversationViewModel vm = new(new RecordingMessageService());
                Task first = vm.LoadSessionAsync(SessionManager.Instance.GetMeta(a.SessionId));
                Task second = vm.LoadSessionAsync(SessionManager.Instance.GetMeta(b.SessionId));
                await Task.WhenAll(first, second);

                // 旧加载作废、新会话不串：形态与标题都是 B 的
                Assert.Equal(b.SessionId, vm.CurrentMeta?.SessionId);
                Assert.Equal(b.Title, vm.Group?.Title);
                Assert.True(vm.IsGroupSession);
            }
            finally
            {
                SessionManager.Instance.Delete(a.SessionId);
                SessionManager.Instance.Delete(b.SessionId);
            }
        });
    }

    [Fact]
    public void LoadingGroupMember_IsMemberByLoadCompletion()
    {
        HeadlessUi.RunAsync(async () =>
        {
            ChatSession group = CreateGroup();
            try
            {
                using ConversationViewModel vm = new(new RecordingMessageService());
                await vm.LoadSessionAsync(SessionManager.Instance.GetGroupMembers(group.SessionId)[0]);

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

    private static ChatSession CreateGroup(string name = "g-load") => GroupChatSessions.Create(name, false,
        [new CharacterData { CharacterName = "A" }, new CharacterData { CharacterName = "B" }], null, null);
}
