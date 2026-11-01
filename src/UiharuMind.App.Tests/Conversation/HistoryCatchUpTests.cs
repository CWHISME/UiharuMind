using System.Collections.ObjectModel;
using Microsoft.Extensions.AI;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 历史追加这条通道的兜底能力：<b>实时流没画到的，历史必须补得上</b>。
///
/// 从前按消息类别一律跳过「内容流产出的那几类」，等于把上屏责任全押在实时流上。
/// 流那一侧一旦漏（观察者没挂上、条目被后台裁掉），这一整轮就一条都补不回来，
/// 只能等轮末对账——实机：唤醒轮跑了 14 分钟，界面停在派活那条，轮末一次性补了 62 条。
/// </summary>
public class HistoryCatchUpTests
{
    /// <summary>别处驱动的一轮（streaming）：流一条没画，历史仍要把已落盘的那几条补出来</summary>
    [Fact]
    public void ExternalTurn_StreamDrewNothing_HistoryStillDraws()
    {
        (ObservableCollection<ConversationItemBase> items, ConversationHistoryRenderer renderer) = Fixture();
        List<ChatMessage> history = [ToolReply("先读一眼。"), new(ChatRole.Assistant, "看完了。")];

        renderer.AppendPersisted(history, 0, ownTurn: false, streaming: true);

        // 思考卡 + 正文 + 工具卡；末尾那条还在流里写，交给流
        Assert.Single(items.OfType<ThinkingItem>());
        Assert.Single(items.OfType<ToolCallItem>());
        Assert.Single(items.OfType<TextConversationItem>());
    }

    /// <summary>
    /// 同一条消息拆出的多个条目（思考 + 正文 + 工具卡共享一个来源）不许在补画时被自己挤掉。
    ///
    /// 去重判据从前是「边加边看界面上有没有」，第二个条目就看见了刚加进去的第一个。
    /// </summary>
    [Fact]
    public void WholeSlice_OneMessageWithSeveralItems_KeepsThemAll()
    {
        (ObservableCollection<ConversationItemBase> items, ConversationHistoryRenderer renderer) = Fixture();
        List<ChatMessage> history = [ToolReply("先读一眼。")];

        renderer.AppendPersisted(history, 0, ownTurn: false, streaming: false);

        Assert.Single(items.OfType<ThinkingItem>());
        Assert.Single(items.OfType<TextConversationItem>());
        Assert.Single(items.OfType<ToolCallItem>());
    }

    /// <summary>流已经画过的那条，历史再补一遍就是两份——这条底线不许因为上面的兜底被削掉</summary>
    [Fact]
    public void ExternalTurn_StreamAlreadyDrew_NoSecondCopy()
    {
        (ObservableCollection<ConversationItemBase> items, ConversationHistoryRenderer renderer) = Fixture();
        ChatMessage reply = ToolReply("先读一眼。");
        List<ChatMessage> history = [reply, new(ChatRole.Assistant, "看完了。")];

        renderer.Append(history, 0, 1, liveTail: true);
        int drawn = items.Count;
        Assert.Equal(3, drawn);

        renderer.AppendPersisted(history, 0, ownTurn: false, streaming: true);

        Assert.Equal(drawn, items.Count);
    }

    /// <summary>
    /// 流只画出了正文与第一张工具卡：缺的那张必须补上。
    ///
    /// 去重按「整条消息」判的话，同一条消息里少一张卡就永远补不回来——工具卡认的是 <c>CallId</c>。
    /// </summary>
    [Fact]
    public void PartialStream_OneToolCardMissing_IsDrawnFromHistory()
    {
        (ObservableCollection<ConversationItemBase> items, ConversationHistoryRenderer renderer) = Fixture();
        ChatMessage reply = new(ChatRole.Assistant,
        [
            new TextContent("跑两条。"),
            new FunctionCallContent("c1", "Read"),
            new FunctionCallContent("c2", "Read"),
        ]);
        List<ChatMessage> history = [reply];

        // 流画过的样子：第一张卡已经在，第二张漏了
        items.Add(new ToolCallItem { CallId = "c1", ToolName = "Read", SourceMessage = reply });

        renderer.AppendPersisted(history, 0, ownTurn: false, streaming: false);

        Assert.Contains(items.OfType<ToolCallItem>(), x => x.CallId == "c2");
        Assert.Single(items.OfType<ToolCallItem>(), x => x.CallId == "c1");
    }

    /// <summary>
    /// 流画出来但还没配对的气泡（来源是空），落盘时先配回来源再判去重——
    /// 不先配的话它看着就是「没画过」，历史再补一条，同一句话两个气泡。
    /// </summary>
    [Fact]
    public void StreamBubbleWithoutSource_IsPairedBeforeDedup()
    {
        (ObservableCollection<ConversationItemBase> items, ConversationHistoryRenderer renderer) = Fixture();
        ChatMessage reply = new(ChatRole.Assistant, "看完了。");
        List<ChatMessage> history = [reply];

        ConversationTranscript transcript = new(items, () => ConversationItemFactory.CreateAssistant(null),
            createUserItems: renderer.CreateUserItems);
        transcript.Apply(new TextContent("看完了。"));

        TextConversationItem bubble = Assert.Single(items.OfType<TextConversationItem>());
        Assert.Null(bubble.SourceMessage);

        renderer.AppendPersisted(history, 0, ownTurn: false, streaming: true);

        Assert.Single(items.OfType<TextConversationItem>());
        Assert.Same(reply, bubble.SourceMessage);
    }

    /// <summary>
    /// 轮内兜底：一轮还在跑也要能把尾部补上，不能等「没人跑」。
    ///
    /// 实机就是等不起——唤醒轮跑 12 分钟，界面冻 12 分钟，轮末一次性补 62 条。
    /// </summary>
    [Fact]
    public void CatchUpTail_AppendsEvenWhileTheTurnIsStillRunning()
    {
        ObservableCollection<ConversationItemBase> items = new();
        ConversationItemActions actions = new(items, new StubHost(), new RecordingMessageService());
        ConversationHistoryRenderer renderer = new(items, actions, () => null, () => false, () => null, () => null);

        ChatSession session = new() { SessionId = "catch-up" };
        session.History.Add(new ChatMessage(ChatRole.Assistant, "第一段"));
        session.History.Add(new ChatMessage(ChatRole.Assistant, "第二段"));
        session.History.Add(new ChatMessage(ChatRole.Assistant, "第三段"));

        // 界面上只有第一条
        items.Add(renderer.Build(session.History, 0, 1, liveTail: true)[0]);

        ReconcileHost host = new(session, renderer) { IsSessionIdle = false };
        ConversationHistoryReconciler reconciler = new(items, new HistoryWindow(), host);

        reconciler.CatchUpTail("test");

        Assert.Equal(3, items.Count);
        Assert.True(host.BuiltLiveTail, "轮内补画必须走 liveTail，否则刚补出来的工具卡会被标成「历史里没有结果」");
    }

    private static ChatMessage ToolReply(string text) =>
        new(ChatRole.Assistant,
        [
            new TextReasoningContent("想一下。"),
            new TextContent(text),
            new FunctionCallContent("c1", "Read"),
        ]);

    private static (ObservableCollection<ConversationItemBase> Items, ConversationHistoryRenderer Renderer) Fixture()
    {
        ObservableCollection<ConversationItemBase> items = new();
        ConversationItemActions actions = new(items, new StubHost(), new RecordingMessageService());
        ConversationHistoryRenderer renderer = new(items, actions, () => null, () => false, () => null, () => null);
        return (items, renderer);
    }

    /// <summary>对账的宿主机位替身：只给对账要用的那几个窄依赖</summary>
    private sealed class ReconcileHost(ChatSession session, ConversationHistoryRenderer renderer)
        : IConversationReconcileHost
    {
        public bool IsSessionIdle { get; init; }
        public bool IsSessionLoading => false;
        public ChatSession? CurrentSession => session;

        /// <summary>补画走的是不是直播口径（轮内必须走这条，否则刚补的卡会被标成没有结果）</summary>
        public bool BuiltLiveTail { get; private set; }

        public void NotifyWindowChanged()
        {
        }

        public List<ConversationItemBase> BuildItems(IReadOnlyList<ChatMessage> history, int from, int to) =>
            renderer.Build(history, from, to);

        public List<ConversationItemBase> BuildLiveTailItems(IReadOnlyList<ChatMessage> history, int from, int to)
        {
            BuiltLiveTail = true;
            return renderer.Build(history, from, to, liveTail: true);
        }

        public void SettleStreamedItems(IReadOnlyList<ChatMessage> history)
        {
        }

        public void RefreshTokenUsage()
        {
        }
    }

    private sealed class StubHost : IConversationItemActionHost
    {
        public ChatSession? Session => null;
        public bool IsGenerating => false;

        public void Rerun(ChatMessage? input)
        {
        }

        public void NotifySessionsChanged()
        {
        }

        public void NotifyItemsWired()
        {
        }
    }
}
