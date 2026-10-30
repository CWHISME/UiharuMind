using System.Text;
using System.Text.Json.Nodes;
using UiharuMind.Core.AI.Reasoning;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 加密思考票据的四条不变量（<see cref="Docs.adr"/> ADR 0100）。
///
/// 这四条是上一版实现翻车的地方，每一条都对应一次真实失败：
///   1. 每条助手消息永久挂自己那张票　　← 上一版只挂最后一条，票逐轮漂移
///   2. 逐轮原样重发　　　　　　　　　　← 上一版用一次即消费，历史字节每轮都变
///   3. 换模型时整条丢弃　　　　　　　　← 上一版只比 model id，网关根本不校验归属
///   4. 跨重启不丢　　　　　　　　　　　← 上一版只在内存，重启即全冷
///
/// 第 4 条第一版只做到「会话存活期内不丢」（内存版），跨进程那条要等落盘。
/// 这里测的是「同一个 store 反复取用不丢」，落盘后同一条断言原样成立。
/// </summary>
public class ReasoningTicketInvariantTests
{
    private const string TicketA = """[{"type":"reasoning.encrypted","data":"AAA","id":"rs_1:rs_a","format":"meta-responses-v1","index":0}]""";
    /// <summary>票据的产出方：CommandCode 上的 muse-spark</summary>
    private static readonly ReasoningGate Producer = new("commandcode", "meta/muse-spark-1.3-contributor");

    private const string TicketB = """[{"type":"reasoning.encrypted","data":"BBB","id":"rs_2:rs_b","format":"meta-responses-v1","index":0}]""";

    /// <summary>
    /// 不变量 1：产出时挂在第 k 条助手消息上，以后每轮都挂在那条——票不会被挪走。
    ///
    /// 上一版的形状是「只挂最后一条」，于是每轮票都往新位置跑，历史里上一轮挂过票的那条
    /// 变成无票形态，字节变了 → 前缀分叉。这里钉的是：位置一旦定下就不再变。
    /// </summary>
    [Fact]
    public void Ticket_StaysOnTheMessageThatProducedIt()
    {
        TicketStore store = new();

        store.Record(0, TicketA, Producer); //第 0 条助手消息产出了票 A
        store.Record(1, TicketB, Producer); //第 1 条助手消息产出了票 B

        //第 0 条还是挂 A，第 1 条挂 B——两张票各归各位
        Assert.Equal(TicketA, store.TicketAt(0));
        Assert.Equal(TicketB, store.TicketAt(1));
    }

    /// <summary>
    /// 不变量 2：一条消息的票据字节从产生那刻起不再变。
    ///
    /// 这是缓存前缀稳定的前提。上一版「用一次即消费」让第 1 轮挂过的位置在第 2 轮变成无票，
    /// 那一条的字节变了 → 从那条起缓存全冷。
    /// </summary>
    [Fact]
    public void Ticket_BytesNeverChangeAcrossRounds()
    {
        TicketStore store = new();
        store.Record(0, TicketA, Producer);

        string first = store.TicketAt(0)!;
        string second = store.TicketAt(0)!;
        string third = store.TicketAt(0)!;

        Assert.Equal(first, second);
        Assert.Equal(second, third);
    }

    /// <summary>
    /// 不变量 3：换模型时整条丢弃——provider 与 model 都要对上。
    ///
    /// 实测网关**不校验**模型归属：把 Meta 的票发给 deepseek 照样 200。
    /// 所以这道门禁是唯一的，漏了不会报错，只会安静地错。
    /// </summary>
    [Fact]
    public void Gate_RejectsWhenProviderOrModelDiffers()
    {
        Assert.True(Producer.Allows("commandcode", "meta/muse-spark-1.3-contributor")); //完全一致
        Assert.False(Producer.Allows("openrouter", "meta/muse-spark-1.3-contributor")); //换供应商
        Assert.False(Producer.Allows("commandcode", "deepseek-v4-flash")); //换模型
    }

    /// <summary>
    /// 不变量 4：票据不丢。
    ///
    /// 第一版是内存版，所以这条只保证「同一个 store 反复取用不丢」；
    /// 跨进程要等落盘。测的是「取用不会把票带走」——上一版正是取一次就清空。
    /// </summary>
    [Fact]
    public void Ticket_SurvivesRepeatedReads()
    {
        TicketStore store = new();
        store.Record(0, TicketA, Producer);

        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(TicketA, store.TicketAt(0));
        }

        Assert.Equal(1, store.Count);
    }

    /// <summary>换模型时不是「删掉票」，而是这一轮不带——历史里的票留着，换回来还能用</summary>
    [Fact]
    public void Gate_SkipsTheTicket_WithoutDeletingIt()
    {
        TicketStore store = new();
        store.Record(0, TicketA, Producer);
        ReasoningGate other = new("openrouter", "deepseek-v4-flash");

        Assert.Null(store.TicketAt(0, other)); //门禁不过，这一轮不带
        Assert.Equal(TicketA, store.TicketAt(0)); //但票本身还在
    }

    /// <summary>
    /// 拼回请求体时，票据挂在<b>原来那条</b>助手消息上。
    ///
    /// 上一次的实现把票挂最后一条，于是每轮位置都变——这条钉住「按序号挂」。
    /// </summary>
    [Fact]
    public void SplicedTicket_LandsOnItsOwnMessage()
    {
        string? rewritten = OpenAICompatibleRequestRewriterTests.Rewrite(
            """
            {"messages":[
              {"role":"user","content":"u1"},
              {"role":"assistant","content":"a1"},
              {"role":"user","content":"u2"},
              {"role":"assistant","content":"a2"}
            ]}
            """,
            new(null, false, null, false, ReasoningTickets: [TicketA, null]));

        JsonArray messages = (JsonArray)JsonNode.Parse(rewritten!)!["messages"]!;

        Assert.Equal(TicketA, messages[1]!["reasoning_details"]!.ToJsonString());
        Assert.Null(messages[3]!["reasoning_details"]); //第二条没票就不该有字段
    }

    /// <summary>缺的位置不补：一条消息都没有票时，请求体里不该冒出空数组</summary>
    [Fact]
    public void MissingSlotsStayAbsent()
    {
        string? rewritten = OpenAICompatibleRequestRewriterTests.Rewrite(
            """{"messages":[{"role":"assistant","content":"a"}]}""",
            new(null, false, null, false, ReasoningTickets: [null]));

        JsonArray messages = (JsonArray)JsonNode.Parse(rewritten!)!["messages"]!;

        Assert.Null(messages[0]!["reasoning_details"]);
    }

    /// <summary>
    /// 明文摘要不进回填——服务端不认摘要回传，带回去是白改请求体。
    /// 摘要走另一条路：摘给界面显示。
    /// </summary>
    [Fact]
    public void Summary_IsReadableButNotReplayed()
    {
        const string frame =
            """{"choices":[{"delta":{"reasoning_details":[{"type":"reasoning.summary","summary":"先看暂存区再决定","id":"rs_1","format":"anthropic-claude-v1","index":0}]}}]}""";

        Assert.True(ReasoningDetailExtractor.TryExtract(Encoding.UTF8.GetBytes(frame), out byte[] value));
        Assert.Equal("先看暂存区再决定", ReasoningDetailExtractor.ReadSummary(value));
    }

    /// <summary>加密条目没有明文摘要：摘出来是空的，不能编</summary>
    [Fact]
    public void EncryptedDetail_HasNoSummary()
    {
        const string frame =
            """{"choices":[{"delta":{"reasoning_details":[{"type":"reasoning.encrypted","data":"AAA"}]}}]}""";

        Assert.True(ReasoningDetailExtractor.TryExtract(Encoding.UTF8.GetBytes(frame), out byte[] value));
        Assert.Null(ReasoningDetailExtractor.ReadSummary(value));
    }
}
