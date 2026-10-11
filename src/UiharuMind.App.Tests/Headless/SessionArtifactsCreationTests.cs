using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 新会话首轮建出本体后产物区跟着就位：不补则产物栏一直是空的，切走切回才靠重载出现。
/// 经 <see cref="ConversationViewModel.OnSessionCreated"/> 进入（挂接执行者那步太重，不进单测）。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class SessionArtifactsCreationTests
{
    [Fact]
    public void NewAgentSessionCreated_BuildsArtifacts()
    {
        HeadlessUi.Run(() =>
        {
            ChatSession created = SessionManager.Instance.StartNewSession(
                new CharacterData { CharacterName = "fresh", IsAgent = true });
            try
            {
                using ConversationViewModel vm = new(new RecordingMessageService());
                Assert.Null(vm.Artifacts);

                vm.OnSessionCreated(created);

                Assert.NotNull(vm.CurrentMeta);
                Assert.NotNull(vm.Artifacts);
                Assert.False(string.IsNullOrEmpty(vm.Artifacts.Title));
            }
            finally
            {
                SessionManager.Instance.Delete(created.SessionId);
            }
        });
    }

    [Fact]
    public void NewChatSessionCreated_BuildsNoArtifacts()
    {
        HeadlessUi.Run(() =>
        {
            ChatSession created = SessionManager.Instance.StartNewSession(
                new CharacterData { CharacterName = "fresh-chat" });
            try
            {
                using ConversationViewModel vm = new(new RecordingMessageService());
                vm.OnSessionCreated(created);

                Assert.NotNull(vm.CurrentMeta);
                Assert.Null(vm.Artifacts);
            }
            finally
            {
                SessionManager.Instance.Delete(created.SessionId);
            }
        });
    }
}
