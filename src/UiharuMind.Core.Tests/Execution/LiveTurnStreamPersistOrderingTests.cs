using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 落盘信号与内容流不同序：框架在泵线程上「交出最后一块更新 → 立刻落盘」，
/// 而这一块经通道到消费方（TurnDriver → Fanout.Apply）要晚一拍。
/// 于是落盘时清掉的待补发缓冲，随后又被这次调用的尾巴（并行工具调用正是流的最后一块）填上——
/// 中途挂上来的观察者从历史读到一份、补发又收到一份。
/// </summary>
public class LiveTurnStreamPersistOrderingTests
{
    private sealed class RecordingSink : ITurnSink
    {
        public List<AIContent> Applied { get; } = new();

        public void Apply(AIContent content) => Applied.Add(content);

        public void CloseSegment() { }

        public void StopRunningToolCalls(string note) { }

        public string? TakeStreamingText() => null;
    }

    [Fact]
    public void TailOfAPersistedCallConsumedAfterThePersistSignal_IsNotReplayedToALatecomer()
    {
        LiveTurnStream stream = new();
        using LiveTurnStream.Scope scope = stream.BeginTurn(null);

        // 泵线程：这次服务调用的最后一块已写进通道，框架随即落盘（SaveAppended → NoteHistoryPersisted）
        stream.NoteHistoryPersisted();
        // 消费方晚一拍才把那一块交给岔口：这两条调用此刻已经在历史里了
        scope.Sink.Apply(new FunctionCallContent("call_post", "PostToGroup"));
        scope.Sink.Apply(new FunctionCallContent("call_read", "Read"));
        // 落盘回调写进通道的边界，排在它们后面
        scope.Sink.Apply(MessageBoundaryContent.Instance);
        // 工具跑完：结果要等下一次调用落盘，此刻确实只在流里
        scope.Sink.Apply(new FunctionResultContent("call_post", "Posted to the group."));
        scope.Sink.Apply(new FunctionResultContent("call_read", "README"));

        RecordingSink latecomer = new();
        using IDisposable subscription = stream.Observe(latecomer);

        Assert.Empty(latecomer.Applied.OfType<FunctionCallContent>());
        Assert.Equal(2, latecomer.Applied.OfType<FunctionResultContent>().Count());
    }
}
