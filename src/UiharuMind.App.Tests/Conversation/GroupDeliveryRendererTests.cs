using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation.Group;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.App.Tests.TestDoubles;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 成员会话里的群投递按发言人画：不拆的话单行 <c>[名字]: 内容</c> 会被 markdown 当链接引用定义吞掉，气泡空白
/// </summary>
public class GroupDeliveryRendererTests
{
    private readonly GroupDeliveryRenderer _renderer = new("member", new Dictionary<string, CharacterData>
    {
        ["Alice"] = new() { CharacterId = "alice", CharacterName = "Alice" },
        ["我"] = new() { CharacterId = "user", CharacterName = "我" },
    }, "我");

    [Fact]
    public void MembersGoLeft_UserGoesRight_SceneIsNarration()
    {
        ChatMessage delivery = new(ChatRole.User, "（这是群聊「会审」。）\n\n[我]: 大家好\n\n[Alice]: 嗯。");
        ChatMessageAnnotations.MarkGroupDelivery(delivery);

        IReadOnlyList<TextConversationItem> items = _renderer.Render(delivery);

        Assert.Equal(3, items.Count);
        Assert.True(items[0].IsNarration);
        Assert.True(items[1].IsUser);
        Assert.Equal("大家好", items[1].Message);
        Assert.False(items[2].IsUser);
        Assert.Equal("Alice", items[2].SenderName);
        Assert.Equal("嗯。", items[2].Message); //前缀剥掉了：不会再被当成链接引用定义
    }

    [Theory]
    [InlineData("[Alice]: 旧投递没有标记", true)]
    [InlineData("（这是群聊「会审」。在场的有……）", true)]
    [InlineData("私聊里我自己打的话", false)]
    [InlineData("[Carol]: 不认识的名字", false)]
    public void UnmarkedLegacyDeliveries_AreRecognizedByShape(string text, bool expected)
    {
        Assert.Equal(expected, _renderer.IsDelivery(new ChatMessage(ChatRole.User, text)));
    }

    /// <summary>
    /// 框架回灌历史时往带附加属性的消息上就地盖 _attribution（来源是我们自己的历史提供器）。
    /// 投递带着标记，于是也被盖上——不豁免的话旧投递在下一次重放时被当成框架注入消息藏掉
    /// </summary>
    [Theory]
    [InlineData("ChatHistory", false)] //历史回灌盖的：还是我们的消息，照画
    [InlineData("AIContextProvider", true)] //真正的注入（todo 快照、模式通知…）：不画
    public void StampedMessage_IsHiddenOnlyWhenReallyInjected(string sourceType, bool injected)
    {
        ChatMessage delivery = new(ChatRole.User, "[Alice]: 嗯。");
        ChatMessageAnnotations.MarkGroupDelivery(delivery);
        // 落盘往返之后的样子
        delivery.AdditionalProperties![ChatMessageAnnotations.Attribution] = System.Text.Json.JsonDocument
            .Parse($$"""{"sourceType":{"value":"{{sourceType}}"},"sourceId":"x"}""").RootElement.Clone();

        Assert.Equal(injected, ConversationItemFactory.IsFrameworkInjected(delivery));
    }

    [Fact]
    public void PrivateChat_ShowsWhatTheUserTyped()
    {
        ChatMessage message = new(ChatRole.User, UiharuMind.Core.AI.Chat.Group.GroupTranscript.WithPrivateNote("你怎么看"));
        ChatMessageAnnotations.MarkGroupPrivate(message);

        Assert.Equal("你怎么看", ConversationItemFactory.DisplayTextOf(message));
        Assert.Equal("你怎么看", ConversationItemFactory.CreateUser("你怎么看", message).Message);
        Assert.False(_renderer.IsDelivery(message)); //私聊不是投递
    }

    /// <summary>
    /// 一轮结束时的配对：一条投递拆出的几只气泡（旁白、成员气泡都不是 IsUser）按正文整组配到落盘那份，
    /// 不能按角色把它们配到别的助手消息上——那会让对账报分歧、整窗重放
    /// </summary>
    [Fact]
    public void SplitBubbles_ArePairedAsOneGroupWithThePersistedCopy()
    {
        System.Collections.ObjectModel.ObservableCollection<ConversationItemBase> items = new();
        UiharuMind.Features.Conversation.ConversationItemActions actions = new(items, new StubHost(), new RecordingMessageService());
        ChatMessage live = new(ChatRole.User, "（场景）\n\n[Alice]: 嗯。");
        ChatMessageAnnotations.MarkGroupDelivery(live);
        ChatMessage earlierReply = new(ChatRole.Assistant, "上一轮");
        foreach (TextConversationItem split in _renderer.Render(live))
        {
            split.SourceMessage = live;
            items.Add(split);
        }

        TextConversationItem reply = new(false) { Message = "好" };
        items.Add(reply);
        ChatMessage persisted = new(ChatRole.User, live.Text);
        ChatMessage landedReply = new(ChatRole.Assistant, "好");

        actions.WireStreamed([earlierReply, persisted, landedReply]);

        Assert.All(items.Take(2), x => Assert.Same(persisted, x.SourceMessage));
        Assert.Same(landedReply, reply.SourceMessage);
    }

    private sealed class StubHost : UiharuMind.Features.Conversation.IConversationItemActionHost
    {
        public ChatSession? Session => null;
        public bool IsGenerating => false;
        public void Rerun(ChatMessage? input) { }
        public void NotifySessionsChanged() { }
        public void NotifyItemsWired() { }
    }
}
