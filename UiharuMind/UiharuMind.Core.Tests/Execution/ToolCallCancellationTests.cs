using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 中途停止会留下孤儿 tool_call：模型那次请求本身成功返回（助手消息带调用，当场落盘），
/// 随后在执行工具的过程中被掐断，结果消息从未产生。OpenAI 与 Anthropic 都要求
/// 带 tool_calls 的助手消息必须有配对结果，否则整个请求 400——这个会话从此发不出话。
/// </summary>
public class ToolCallCancellationTests
{
    private static ChatMessage Call(params string[] callIds)
    {
        return new ChatMessage(ChatRole.Assistant,
            callIds.Select(AIContent (x) => new FunctionCallContent(x, "ask_vision", null)).ToList());
    }

    private static ChatMessage Result(string callId)
    {
        return new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, "ok")]);
    }

    [Fact]
    public void UnansweredCall_IsFound()
    {
        List<ChatMessage> history = [new(ChatRole.User, "看看这张图"), Call("a")];

        Assert.Equal(["a"], ToolCallCancellation.FindUnansweredAtTail(history));
    }

    [Fact]
    public void AnsweredCall_IsLeftAlone()
    {
        List<ChatMessage> history = [new(ChatRole.User, "看看"), Call("a"), Result("a")];

        Assert.Empty(ToolCallCancellation.FindUnansweredAtTail(history));
    }

    [Fact]
    public void MixedTurn_KeepsCallOrderAndOnlyReportsUnanswered()
    {
        //一轮里跑了三次工具:前两次拿到结果,第三次停在半路
        List<ChatMessage> history =
        [
            new(ChatRole.User, "开工"),
            Call("a"), Result("a"),
            Call("b", "c"), Result("b"),
        ];

        Assert.Equal(["c"], ToolCallCancellation.FindUnansweredAtTail(history));
    }

    [Fact]
    public void ParallelCalls_AreAllClosed()
    {
        List<ChatMessage> history = [new(ChatRole.User, "开工"), Call("a", "b")];

        Assert.Equal(["a", "b"], ToolCallCancellation.FindUnansweredAtTail(history));
    }

    [Fact]
    public void OrphanBuriedBehindAFinishedTurn_IsNotTouched()
    {
        //补出来的结果只能追加到末尾,那对更早的孤儿是错位的,宁可不动(它属于本机制上线前的旧账)
        List<ChatMessage> history =
        [
            Call("old"), //没有结果
            new(ChatRole.Assistant, "这轮后来正常说完了"),
            new(ChatRole.User, "下一轮"),
        ];

        Assert.Empty(ToolCallCancellation.FindUnansweredAtTail(history));
    }

    [Fact]
    public void CancelledResult_IsRecognisable()
    {
        //判据必须落在正文上:FunctionResultContent.Exception 带 [JsonIgnore],存盘再读回来就没了
        Assert.True(ToolCallCancellation.IsCancelled(new FunctionResultContent("a", ToolCallCancellation.ResultText)));
        Assert.False(ToolCallCancellation.IsCancelled(new FunctionResultContent("a", "ok")));
        Assert.False(ToolCallCancellation.IsCancelled(new FunctionResultContent("a", null)));
    }
}
