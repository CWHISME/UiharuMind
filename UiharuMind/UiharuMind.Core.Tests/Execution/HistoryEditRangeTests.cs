using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 删除/截断历史时的配对闭合。
///
/// 唯一要守住的不变量：改完之后，每个 tool_call 都有配对结果，每个结果都有配对调用。
/// 破了它，这个会话下一次请求就 400，从此发不出话。
///
/// 这里刻意用「正文 + 工具调用同处一条 assistant 消息」的形态构造历史——那是本仓
/// 最常见的真实形态（见 <c>ChatContentNormalizerTests</c>），也正是按内容形状猜边界
/// 的做法必然踩空的地方。
/// </summary>
public class HistoryEditRangeTests
{
    private static ChatMessage User(string text) => new(ChatRole.User, text);

    private static ChatMessage Reply(string text) => new(ChatRole.Assistant, text);

    /// <summary>本仓最常见的 assistant 形态:正文与工具调用同处一条消息</summary>
    private static ChatMessage ReplyWithCalls(string text, params string[] callIds)
    {
        List<AIContent> contents = [new TextContent(text)];
        contents.AddRange(callIds.Select(AIContent (x) => new FunctionCallContent(x, "read_file", null)));
        return new ChatMessage(ChatRole.Assistant, contents);
    }

    private static ChatMessage Calls(params string[] callIds) =>
        new(ChatRole.Assistant, callIds.Select(AIContent (x) => new FunctionCallContent(x, "read_file", null)).ToList());

    private static ChatMessage Results(params string[] callIds) =>
        new(ChatRole.Tool, callIds.Select(AIContent (x) => new FunctionResultContent(x, "ok")).ToList());

    /// <summary>剩余历史里悬空的调用与孤儿结果——不变量断言用</summary>
    private static (string[] Calls, string[] Results) Dangling(IEnumerable<ChatMessage> history)
    {
        List<ChatMessage> list = history.ToList();
        string[] calls = list.SelectMany(x => x.Contents).OfType<FunctionCallContent>().Select(x => x.CallId).ToArray();
        string[] results = list.SelectMany(x => x.Contents).OfType<FunctionResultContent>().Select(x => x.CallId).ToArray();
        return (calls.Except(results).ToArray(), results.Except(calls).ToArray());
    }

    private static List<ChatMessage> After(List<ChatMessage> history, IReadOnlyList<ChatMessage> removed)
    {
        HashSet<ChatMessage> doomed = new(removed);
        return history.Where(x => !doomed.Contains(x)).ToList();
    }

    /// <summary>
    /// 删一条不牵涉工具的回复：只删它自己，前面的工具往返原样留下。
    /// 最小删除是有意的——多删一条就是用户没要求的数据丢失。
    /// </summary>
    [Fact]
    public void PlainReply_RemovesOnlyItself()
    {
        ChatMessage final = Reply("查完了");
        List<ChatMessage> history = [User("做事"), Calls("c1"), Results("c1"), final];

        IReadOnlyList<ChatMessage> removed = HistoryEditRange.ResolveDeletion(history, final);

        Assert.Single(removed);
        Assert.Same(final, removed[0]);
        Assert.Equal(([], []), Dangling(After(history, removed)));
    }

    /// <summary>
    /// 删「正文 + 工具调用」同体的消息：它的结果必须跟着走，否则结果成孤儿。
    /// </summary>
    [Fact]
    public void ReplyCarryingCall_TakesItsResultAlong()
    {
        ChatMessage step = ReplyWithCalls("我来查一下", "c1");
        ChatMessage result = Results("c1");
        List<ChatMessage> history = [User("做事"), step, result, Reply("查完了")];

        IReadOnlyList<ChatMessage> removed = HistoryEditRange.ResolveDeletion(history, step);

        Assert.Equal([step, result], removed);
        Assert.Equal(([], []), Dangling(After(history, removed)));
    }

    /// <summary>
    /// 删「正文 + 工具调用」同体消息<b>后面</b>那条回复：结果属于前面那次调用，
    /// 不能被当成后面这条回复的处理过程一起带走——带走了就留下悬空的 c1。
    /// 这正是按「向上扫到带正文的消息为止」猜边界会踩的坑。
    /// </summary>
    [Fact]
    public void ReplyAfterToolTraffic_DoesNotStealThePrecedingResult()
    {
        ChatMessage final = Reply("查完了");
        List<ChatMessage> history = [User("做事"), ReplyWithCalls("我来查一下", "c1"), Results("c1"), final];

        IReadOnlyList<ChatMessage> removed = HistoryEditRange.ResolveDeletion(history, final);

        Assert.Single(removed);
        Assert.Equal(([], []), Dangling(After(history, removed)));
    }

    /// <summary>
    /// 删工具结果消息：反向也要闭合，把发起它的调用消息一起带走。
    /// </summary>
    [Fact]
    public void ToolResult_TakesItsCallAlong()
    {
        ChatMessage calls = Calls("c1");
        ChatMessage results = Results("c1");
        List<ChatMessage> history = [User("做事"), calls, results, Reply("完成")];

        IReadOnlyList<ChatMessage> removed = HistoryEditRange.ResolveDeletion(history, results);

        Assert.Equal([calls, results], removed);
        Assert.Equal(([], []), Dangling(After(history, removed)));
    }

    /// <summary>
    /// 并行调用：一条消息发起 c1/c2，结果分落两条消息。删任意一头，三条必须一起走
    /// ——留下半组结果同样会被拒。
    /// </summary>
    [Fact]
    public void ParallelCalls_CloseAsOneGroup()
    {
        ChatMessage calls = Calls("c1", "c2");
        ChatMessage result1 = Results("c1");
        ChatMessage result2 = Results("c2");
        List<ChatMessage> history = [User("做事"), calls, result1, result2, Reply("完成")];

        IReadOnlyList<ChatMessage> removed = HistoryEditRange.ResolveDeletion(history, result1);

        Assert.Equal([calls, result1, result2], removed);
        Assert.Equal(([], []), Dangling(After(history, removed)));
    }

    /// <summary>不在历史里的消息（同一条消息的第二个气泡已把它删掉了）不做任何事</summary>
    [Fact]
    public void MessageNotInHistory_ResolvesToNothing()
    {
        List<ChatMessage> history = [User("做事"), Reply("完成")];

        Assert.Empty(HistoryEditRange.ResolveDeletion(history, Reply("完成")));
    }

    /// <summary>
    /// 分叉截断点落在「正文 + 工具调用」消息之后、结果之前：必须把结果一并保留，
    /// 否则分出去的会话一开口就是 400。
    /// </summary>
    [Fact]
    public void Truncation_KeepsTheResultOfAKeptCall()
    {
        List<ChatMessage> history = [User("做事"), ReplyWithCalls("我来查一下", "c1"), Results("c1"), Reply("完成")];

        int keep = HistoryEditRange.ExpandKeptPrefix(history, 2); //只保留到那条带调用的回复

        Assert.Equal(3, keep);
        Assert.Equal(([], []), Dangling(history.Take(keep)));
    }

    /// <summary>补进来的消息若又带来新调用，边界继续往后推</summary>
    [Fact]
    public void Truncation_KeepsChainingUntilEveryCallIsAnswered()
    {
        List<ChatMessage> history =
        [
            User("做事"),
            ReplyWithCalls("第一步", "c1"),
            new(ChatRole.Tool, [new FunctionResultContent("c1", "ok"), new FunctionCallContent("c2", "read_file", null)]),
            Results("c2"),
            Reply("完成"),
        ];

        int keep = HistoryEditRange.ExpandKeptPrefix(history, 2);

        Assert.Equal(4, keep);
        Assert.Equal(([], []), Dangling(history.Take(keep)));
    }

    /// <summary>截断点本就干净时不多留</summary>
    [Fact]
    public void Truncation_LeavesACleanBoundaryAlone()
    {
        List<ChatMessage> history = [User("做事"), Calls("c1"), Results("c1"), Reply("完成")];

        Assert.Equal(3, HistoryEditRange.ExpandKeptPrefix(history, 3));
        Assert.Equal(0, HistoryEditRange.ExpandKeptPrefix(history, 0));
        Assert.Equal(4, HistoryEditRange.ExpandKeptPrefix(history, 99));
    }
}
