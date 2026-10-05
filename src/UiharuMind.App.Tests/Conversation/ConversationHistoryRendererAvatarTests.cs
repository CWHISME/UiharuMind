using System.Collections.ObjectModel;
using Microsoft.Extensions.AI;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 群里化身替用户说的那句（ADR 0055）：署名"化身"、头像走化身自己的卡，正文不动
/// </summary>
public class ConversationHistoryRendererAvatarTests
{
    private readonly ConversationHistoryRenderer _renderer;

    public ConversationHistoryRendererAvatarTests()
    {
        ObservableCollection<ConversationItemBase> items = new();
        ConversationItemActions actions = new(items, new StubHost(), new RecordingMessageService());
        _renderer = new ConversationHistoryRenderer(items, actions, () => null, () => true, () => null, () => null);
    }

    [Fact]
    public void AvatarPost_ShowsAvatarSenderAndIcon_AndLeavesTheBodyAlone()
    {
        CharacterData avatar = new()
        {
            CharacterId = "avatar-icon-test",
            CharacterName = "化身",
            CharacterIcon = "avares://UiharuMind/Assets/Avatars/GroupAvatarDefault.png",
        };
        ChatSession avatarSession = new("化身", avatar) { IsTransient = true };
        SessionManager.Instance.Add(avatarSession);
        try
        {
            ChatMessage post = new(ChatRole.User, "就用 A 方案") { AuthorName = "黑猫" };
            ChatMessageAnnotations.MarkGroupAvatarPost(post, avatarSession.SessionId);

            TextConversationItem item = Assert.Single(_renderer.CreateUserItems(post));

            Assert.Equal(Loc.Text(LangKey.GroupAvatarSender), item.SenderName);
            Assert.Same(IconUtils.GetCharacterBitmapOrDefault(avatar), item.Icon);
            Assert.Equal("就用 A 方案", item.Message);
        }
        finally
        {
            SessionManager.Instance.Delete(avatarSession.SessionId);
        }
    }

    [Fact]
    public void OrdinaryUserPost_KeepsItsSender()
    {
        ChatMessage post = new(ChatRole.User, "我自己说的");

        TextConversationItem item = Assert.Single(_renderer.CreateUserItems(post));

        Assert.Equal(Loc.Text(LangKey.AgentSenderUser), item.SenderName);
    }

    private sealed class StubHost : IConversationItemActionHost
    {
        public ChatSession? Session => null;
        public bool IsGenerating => false;
        public void Rerun(ChatMessage? input) { }
        public void NotifySessionsChanged() { }
        public void NotifyItemsWired() { }
    }
}
