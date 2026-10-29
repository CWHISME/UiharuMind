using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 空态输入草稿的消费信号：首轮建出会话本体的那一刻发出，并带上新会话形态——
/// 页面壳靠它清对应那一档槽。此前是拿 SessionsChanged 反推（那个信号不带是谁发的），
/// 只能再叠「空态出生」名单与「当前展示」判据，这里把契约钉住。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class EmptyDraftConsumedTests
{
    [Fact]
    public void SessionCreatedFromEmpty_RaisesWithSessionForm()
    {
        HeadlessUi.Run(() =>
        {
            ChatSession created = SessionManager.Instance.StartNewSession(
                new CharacterData { CharacterName = "draft-form", IsAgent = true });
            try
            {
                using ConversationViewModel vm = new(new RecordingMessageService());
                vm.NewSessionIsAgentForm = true;
                List<bool> forms = new();
                vm.EmptyDraftConsumed += forms.Add;

                vm.OnSessionCreated(created);

                Assert.Equal([true], forms);
            }
            finally
            {
                SessionManager.Instance.Delete(created.SessionId);
            }
        });
    }
}
