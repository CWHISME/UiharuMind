using System.Collections.ObjectModel;
using Microsoft.Extensions.AI;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.ToolCall;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 别处驱动的一轮跑到一半才打开窗口（群成员会话、子会话）：历史回放画出上一次调用的工具卡，
/// 实时流补发又把同一批调用画一遍，结果按 CallId 落到<b>后画的那张</b>上，
/// 先画的那张一直转圈，轮末被 StopRunningToolCalls 收成红色「[cancelled]」。
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ObservedTurnLateJoinToolCardTests
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
    public void LateObserver_ParallelToolCardsAreDrawnOnceAndNotCancelled() => HeadlessUi.Run(() =>
    {
        // 第一次服务调用：一条助手消息、两条并行调用，已落盘；结果随下一次调用落盘，此刻历史里没有
        List<ChatMessage> history =
        [
            new(ChatRole.User, "发个言"),
            new(ChatRole.Assistant,
            [
                new FunctionCallContent("call_post", "PostToGroup"),
                new FunctionCallContent("call_read", "Read"),
            ]),
        ];

        // 执行侧（无头，驱动落点为空）：泵线程交出最后一块就落盘，消费方晚一拍才把它交给岔口
        LiveTurnStream stream = new();
        LiveTurnStream.Scope scope = stream.BeginTurn(null);
        stream.NoteHistoryPersisted();
        foreach (AIContent content in history[1].Contents) scope.Sink.Apply(content);
        scope.Sink.Apply(MessageBoundaryContent.Instance);
        scope.Sink.Apply(new FunctionResultContent("call_post", "Posted to the group."));
        scope.Sink.Apply(new FunctionResultContent("call_read", "README"));

        // 此刻打开窗口：与 ConversationViewModel 装载同序——先回放历史（有轮在跑，liveTail），再挂观察
        ObservableCollection<ConversationItemBase> items = new();
        ConversationHistoryRenderer renderer = new(items,
            new ConversationItemActions(items, new StubHost(), new RecordingMessageService()),
            () => null, () => false, () => null, () => null);
        renderer.Append(history, 0, history.Count, liveTail: true);
        ConversationTranscript transcript = new(items, () => ConversationItemFactory.CreateAssistant(null));
        using IDisposable observation = stream.Observe(transcript);

        // 第二次调用跑完、轮末收尾（TurnDriver.finally）
        scope.Sink.StopRunningToolCalls(ToolCallCancellation.ResultText);
        scope.Dispose();

        List<ToolCallItem> cards = items.OfType<ToolCallItem>().ToList();
        Assert.Equal(["call_post", "call_read"], cards.Select(x => x.CallId));
        Assert.All(cards, x => Assert.True(x.IsSuccess, $"{x.ToolName} card: {x.ResultText}"));
    });
}
