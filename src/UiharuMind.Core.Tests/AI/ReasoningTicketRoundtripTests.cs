using System.Text;
using System.Text.Json.Nodes;
using UiharuMind.Core.AI.Net.Request;
using UiharuMind.Core.AI.Net.Response;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 加密思考凭证（<c>reasoning_details</c>）的往返：从响应切下来、原样拼回请求。
///
/// 三条断言各自钉一处：<b>原样</b>（密文一个字节都不能变，变了服务端就不认）、
/// <b>对位</b>（按助手消息顺序挂回去，别串到别人身上）、
/// <b>不越界</b>（缺的位置留空，不要凭空给某条消息塞一张票）。
/// </summary>
public class ReasoningTicketRoundtripTests
{
    private const string Ticket =
        """[{"type":"reasoning.encrypted","data":"Q-PaD+/=Zm9v","id":"rs_1:rs_2","format":"meta-responses-v1","index":0}]""";

    /// <summary>票据原样落在它该在的那条助手消息上，与明文思考并存互不干扰</summary>
    [Fact]
    public void Ticket_IsSplicedVerbatimOntoItsAssistantMessage()
    {
        string? rewritten = OpenAICompatibleRequestRewriterTests.Rewrite(
            """{"messages":[{"role":"user","content":"u"},{"role":"assistant","content":"a"}]}""",
            new RequestRewrite(null, false, ["想了想"], false, ReasoningTickets: [Ticket]));

        JsonArray messages = (JsonArray)JsonNode.Parse(rewritten!)!["messages"]!;
        JsonArray details = (JsonArray)messages[1]!["reasoning_details"]!;

        Assert.Equal("reasoning.encrypted", details[0]!["type"]!.GetValue<string>());
        Assert.Equal("Q-PaD+/=Zm9v", details[0]!["data"]!.GetValue<string>()); //密文逐字相同
        Assert.Equal("meta-responses-v1", details[0]!["format"]!.GetValue<string>());
        Assert.Equal("想了想", messages[1]!["reasoning_content"]!.GetValue<string>()); //明文那条没被顶掉
        Assert.Null(messages[0]!["reasoning_details"]); //用户消息不挂
    }

    /// <summary>缺的位置不补：三条助手消息只有最后一条有票，前两条不该冒出空数组</summary>
    [Fact]
    public void Ticket_MissingSlotsStayAbsent()
    {
        string? rewritten = OpenAICompatibleRequestRewriterTests.Rewrite(
            """
            {"messages":[
              {"role":"assistant","content":"a1"},
              {"role":"user","content":"u"},
              {"role":"assistant","content":"a2"}
            ]}
            """,
            new RequestRewrite(null, false, null, false, ReasoningTickets: [null, Ticket]));

        JsonArray messages = (JsonArray)JsonNode.Parse(rewritten!)!["messages"]!;

        Assert.Null(messages[0]!["reasoning_details"]);
        Assert.Equal("reasoning.encrypted", messages[2]!["reasoning_details"]![0]!["type"]!.GetValue<string>());
    }

    /// <summary>没有票据时请求体一个字节都不该变（不然每次请求都要冷一次前缀缓存）</summary>
    [Fact]
    public void NoTicket_LeavesTheBodyUntouched()
    {
        const string body = """{"messages":[{"role":"assistant","content":"a"}]}""";

        string? rewritten = OpenAICompatibleRequestRewriterTests.Rewrite(
            body, new RequestRewrite(null, false, null, false));

        Assert.Null(rewritten); //null 表示「不必改写」，上游会直接发原文
    }

    /// <summary>
    /// 从一帧里切出整个数组原文。帧里前后还有别的字段，周边括号不能干扰切分。
    /// </summary>
    [Fact]
    public void Grabber_ExtractsTheWholeArrayFromAFrame()
    {
        string line =
            """data: {"choices":[{"delta":{"content":null,"reasoning_details":""" + Ticket + """}}]}""";

        Assert.True(ReasoningDetailsGrabber.TryExtract(Encoding.UTF8.GetBytes(line), out byte[] value));
        Assert.Equal(Ticket, Encoding.UTF8.GetString(value));
    }

    /// <summary>字符串里的引号与方括号不算结构：数括号时得跳过转义与字符串内部</summary>
    [Fact]
    public void Grabber_IgnoresBracketsInsideStrings()
    {
        string line = """{"reasoning_details":[{"data":"a]b\"c[d","index":0}]}""";

        Assert.True(ReasoningDetailsGrabber.TryExtract(Encoding.UTF8.GetBytes(line), out byte[] value));
        Assert.Equal("""[{"data":"a]b\"c[d","index":0}]""", Encoding.UTF8.GetString(value));
    }

    /// <summary>被截断的半截数组当没找到：残缺的票发回去比不发更糟</summary>
    [Fact]
    public void Grabber_RejectsTruncatedArray()
    {
        string line = """{"reasoning_details":[{"type":"reasoning.encrypted","data":"QQ""";

        Assert.False(ReasoningDetailsGrabber.TryExtract(Encoding.UTF8.GetBytes(line), out _));
    }

    /// <summary>没有这个键就是没货，不该返回空数组冒充</summary>
    [Fact]
    public void Grabber_NoKeyMeansNothing()
    {
        Assert.False(ReasoningDetailsGrabber.TryExtract("""{"reasoning_content":"明文"}"""u8, out _));
    }
}
