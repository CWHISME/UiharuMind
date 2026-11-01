using System.Collections.ObjectModel;
using Microsoft.Extensions.AI;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 回放「加密思考」卡：<b>标记 + 密文两样齐全</b>才补。
///
/// 只有标记的老消息是旧判据留下的——那一版把「usage 报了思考 token」当成「正文被加密」，
/// 明文模型在工具调用发上就是这种形态（实测 deepseek 报 1）。这种消息一条思考内容都没有，
/// 按标记补卡就是凭空冒出一张「加密思考」卡（ADR 0100 修订）。
/// </summary>
public class ConversationHistoryEncryptedThinkingTests
{
    private sealed class StubHost : IConversationItemActionHost
    {
        public ChatSession? Session => null;
        public bool IsGenerating => false;
        public void Rerun(ChatMessage? input) { }
        public void NotifySessionsChanged() { }
        public void NotifyItemsWired() { }
    }

    /// <summary>旧标记（无密文）：回放不补卡——那只是 usage 在报计数</summary>
    [Fact]
    public void Replay_SkipsBodilessCard_WhenOnlyLegacyMarkExists()
    {
        ObservableCollection<ConversationItemBase> items = new();
        ConversationHistoryRenderer renderer = Renderer(items);

        ChatMessage reply = new(ChatRole.Assistant, [new FunctionCallContent("c1", "Read")]);
        ChatMessageAnnotations.WriteThinkingUnavailable(reply, 1);
        List<ChatMessage> history = [new(ChatRole.User, "开工"), reply];

        renderer.Append(history, 0, history.Count, liveTail: false);

        Assert.Empty(items.OfType<ThinkingItem>());
    }

    /// <summary>带密文：卡照补（跨重启不丢那条不变量），标题是没有正文的那种</summary>
    [Fact]
    public void Replay_RestoresBodilessCard_WhenCiphertextIsPresent()
    {
        ObservableCollection<ConversationItemBase> items = new();
        ConversationHistoryRenderer renderer = Renderer(items);

        ChatMessage reply = new(ChatRole.Assistant,
        [
            new TextReasoningContent(string.Empty) { ProtectedData = "BLOB" },
            new FunctionCallContent("c1", "Read"),
        ]);
        ChatMessageAnnotations.WriteThinkingUnavailable(reply, 790);
        List<ChatMessage> history = [new(ChatRole.User, "开工"), reply];

        renderer.Append(history, 0, history.Count, liveTail: false);

        ThinkingItem card = Assert.Single(items.OfType<ThinkingItem>());
        Assert.True(card.IsContentUnavailable);
    }

    private static ConversationHistoryRenderer Renderer(ObservableCollection<ConversationItemBase> items)
    {
        ConversationItemActions actions = new(items, new StubHost(), new RecordingMessageService());
        return new ConversationHistoryRenderer(items, actions, () => null, () => false, () => null, () => null);
    }
}
