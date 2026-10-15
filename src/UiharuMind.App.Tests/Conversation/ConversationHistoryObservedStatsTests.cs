using System.Collections.ObjectModel;
using Microsoft.Extensions.AI;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 旁观别处那一轮（子会话窗口看后台委派）：驱动方每次服务调用落盘后就把统计盖进内存历史，
/// 落盘追加信号来到时，上一批冻成纯字数/空白的卡要就地升级成存档值，而不是等到轮末。
/// 只读不写，幂等；正在流的那几张来源还没配好，认领时自动跳过。
/// </summary>
public class ConversationHistoryObservedStatsTests
{
    private sealed class StubHost : IConversationItemActionHost
    {
        public ChatSession? Session => null;
        public bool IsGenerating => false;
        public void Rerun(ChatMessage? input) { }
        public void NotifySessionsChanged() { }
        public void NotifyItemsWired() { }
    }

    [Fact]
    public void AppendAlongsideStream_AdoptsThinkingArchiveStampedAfterReplay()
    {
        ObservableCollection<ConversationItemBase> items = new();
        ConversationItemActions actions = new(items, new StubHost(), new RecordingMessageService());
        ConversationHistoryRenderer renderer = new(items, actions, () => null, () => false, () => null, () => null);

        ChatMessage reply = new(ChatRole.Assistant, [new TextReasoningContent("先想想。")]);
        List<ChatMessage> history = [new(ChatRole.User, "开工"), reply];

        // 运行中打开窗口：回放已落盘的那段，存档还没盖（驱动方的边界盖章随后才落下），只能定格字数
        renderer.Append(history, 0, history.Count, liveTail: true);
        ThinkingItem card = Assert.Single(items.OfType<ThinkingItem>());
        Assert.Equal(
            string.Format(Loc.Text(LangKey.AgentThinkingCharsFormat), card.FullLength.ToString("N0")),
            card.StatsText);

        // 驱动方的边界盖章落到同一批内存消息上；下一拍落盘追加（内容流产出的不重画，只配对+认领）
        ChatMessageAnnotations.WriteThinkingStats(reply, 2000, 4);
        renderer.AppendPersisted(history, history.Count, ownTurn: false, streaming: true);

        Assert.Equal(ThinkingItem.FormatStats(TimeSpan.FromSeconds(2), 4, isFinal: true), card.StatsText);
    }

    [Fact]
    public void AppendAlongsideStream_AdoptsToolArchiveStampedAfterReplay()
    {
        ObservableCollection<ConversationItemBase> items = new();
        ConversationItemActions actions = new(items, new StubHost(), new RecordingMessageService());
        ConversationHistoryRenderer renderer = new(items, actions, () => null, () => false, () => null, () => null);

        ChatMessage call = new(ChatRole.Assistant, [new FunctionCallContent("c1", "Read")]);
        List<ChatMessage> history = [new(ChatRole.User, "开工"), call];

        ConversationTranscript live = new(items, () => ConversationItemFactory.CreateAssistant(null));
        live.Apply(new FunctionCallContent("c1", "Read"));
        live.Apply(new FunctionResultContent("c1", "内容"));

        // 第一拍：存档还没盖，卡上只有 live 的 provisional
        renderer.AppendPersisted(history, 1, ownTurn: false, streaming: true);
        ToolCallItem card = Assert.Single(items.OfType<ToolCallItem>());
        Assert.NotEqual(string.Empty, card.DurationText);

        // 驱动方的边界盖章落下；下一拍把它换成存档值
        ChatMessageAnnotations.WriteToolDuration(call, 9500);
        renderer.AppendPersisted(history, history.Count, ownTurn: false, streaming: true);

        Assert.Equal(ToolCallItem.FormatDuration(TimeSpan.FromMilliseconds(9500)), card.DurationText);
    }
}
