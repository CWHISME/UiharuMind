using System.Text.Json.Nodes;
using UiharuMind.Core.AI.Net.Request;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 按序回填的数量闸门。
///
/// 回填靠「第几条助手消息」定位，而这个序号在两侧分别数出来：一侧是收集思考时遍历的消息列表，
/// 另一侧是 SDK 序列化出的 <c>messages</c>。中间隔着序列化这个黑盒——它一旦丢弃、合并或补进一条，
/// 后面所有位置整体错位，而错位不会换来 400，只会让服务端把思考配到别的消息上，安静地脏掉。
///
/// 所以口径是宁缺毋滥：数量对不上就整段不发。这组测试钉的就是「对不上时确实一条都没发」。
/// </summary>
public class ReasoningReplayGateTests
{
    private const string TwoAssistants = """
        {"messages":[{"role":"user","content":"q1"},
                     {"role":"assistant","content":"a1"},
                     {"role":"user","content":"q2"},
                     {"role":"assistant","content":"a2"}]}
        """;

    [Fact]
    public void MatchingCount_ReplaysEveryPosition()
    {
        string? rewritten = ChatCompletionsRequestRewriterTests.Rewrite(TwoAssistants,
            new RequestRewrite(null, false, ["think-1", "think-2"], false));

        JsonArray messages = (JsonArray)JsonNode.Parse(rewritten!)!["messages"]!;
        Assert.Equal("think-1", messages[1]!["reasoning_content"]!.GetValue<string>());
        Assert.Equal("think-2", messages[3]!["reasoning_content"]!.GetValue<string>());
    }

    /// <summary>
    /// 收集到三条、wire 上只有两条：说明某一侧多算了一条，位置关系已经不可信——
    /// 一条都不许发。发出去的结果是「think-1 配给 a1、think-2 配给 a2、think-3 丢弃」，
    /// 看着没报错，实际已经把思考错位了。
    /// </summary>
    [Fact]
    public void MoreCollectedThanOnTheWire_SendsNothing()
    {
        string? rewritten = ChatCompletionsRequestRewriterTests.Rewrite(TwoAssistants,
            new RequestRewrite(null, false, ["think-1", "think-2", "think-3"], false));

        Assert.DoesNotContain("reasoning_content", rewritten);
    }

    /// <summary>收集得比 wire 上少同样不可信（wire 上多出一条，后面全错位）</summary>
    [Fact]
    public void FewerCollectedThanOnTheWire_SendsNothing()
    {
        string? rewritten = ChatCompletionsRequestRewriterTests.Rewrite(TwoAssistants,
            new RequestRewrite(null, false, ["think-1"], false));

        Assert.DoesNotContain("reasoning_content", rewritten);
    }

    /// <summary>票据走同一道闸门——密文错配比明文更糟，服务端可能直接拒</summary>
    [Fact]
    public void TicketCountMismatch_SendsNoTicket()
    {
        string? rewritten = ChatCompletionsRequestRewriterTests.Rewrite(TwoAssistants,
            new RequestRewrite(null, false, null, false, ReasoningTickets: ["""{"type":"reasoning.encrypted"}"""]));

        Assert.DoesNotContain("reasoning_details", rewritten);
    }

    /// <summary>票据数量对得上时照常发（闸门不能把正常路径也挡了）</summary>
    [Fact]
    public void TicketCountMatches_SendsTickets()
    {
        string? rewritten = ChatCompletionsRequestRewriterTests.Rewrite(TwoAssistants,
            new RequestRewrite(null, false, null, false,
                ReasoningTickets: ["""{"id":"t1"}""", """{"id":"t2"}"""]));

        JsonArray messages = (JsonArray)JsonNode.Parse(rewritten!)!["messages"]!;
        Assert.NotNull(messages[1]!["reasoning_details"]);
        Assert.NotNull(messages[3]!["reasoning_details"]);
    }

    /// <summary>
    /// 「补空串」不按位置取内容，天然不会错配，不该被闸门挡住——
    /// 否则 DeepSeek 那类「字段必须在」的模型会直接 400
    /// </summary>
    [Fact]
    public void FillEmptyReasoning_IsNotGated()
    {
        string? rewritten = ChatCompletionsRequestRewriterTests.Rewrite(TwoAssistants,
            new RequestRewrite(null, false, null, false, true));

        JsonArray messages = (JsonArray)JsonNode.Parse(rewritten!)!["messages"]!;
        Assert.Equal("", messages[1]!["reasoning_content"]!.GetValue<string>());
        Assert.Equal("", messages[3]!["reasoning_content"]!.GetValue<string>());
    }
}
