using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Reasoning;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 回填计划：一次遍历同时产出「要发出去的消息」「明文思考清单」「加密票据清单」。
///
/// 这组测试钉的是<b>三者的对齐关系</b>——序号只有一个来源（助手消息的出现顺序），
/// 两条清单的长度必须都等于助手消息条数。早先这是两段独立的 <c>foreach</c> 各自
/// <c>index++</c>，谁漏一个边界条件就静默错位，而错位不会 400，只会让服务端把思考
/// 配到别的消息上。
/// </summary>
public class ReplayPlanTests
{
    private static readonly ReasoningGate Current = new("provider-a", "model-a");

    private static ChatMessage WithTicket(string ticket, string text = "")
    {
        TextReasoningContent reasoning = new(text) { ProtectedData = ticket };
        ReasoningTicketCarrier.StampProducer(reasoning, Current);
        return new ChatMessage(ChatRole.Assistant, [reasoning]);
    }

    /// <summary>两条清单按同一次遍历产出，长度都等于助手消息条数</summary>
    [Fact]
    public void BothLists_AlignWithTheAssistantMessages()
    {
        List<ChatMessage> history =
        [
            new(ChatRole.User, "q1"),
            new(ChatRole.Assistant, [new TextReasoningContent("想一")]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", "r")]),
            WithTicket("""[{"type":"reasoning.encrypted","data":"B"}]"""),
            new(ChatRole.Assistant, "答"),
        ];

        ReplayPlan plan = ReplayPlan.For(history, Current);

        Assert.Equal(3, plan.Reasoning!.Count);
        Assert.Equal("想一", plan.Reasoning[0]);
        Assert.Null(plan.Reasoning[1]);
        Assert.Null(plan.Reasoning[2]);

        Assert.Equal(3, plan.Tickets!.Count);
        Assert.Null(plan.Tickets[0]);
        Assert.NotNull(plan.Tickets[1]);
        Assert.Null(plan.Tickets[2]);
    }

    /// <summary>门禁不过的票：发出去的那份摘掉，历史里的原消息一个字节不动</summary>
    [Fact]
    public void BlockedTicket_LeavesTheHistoryUntouched()
    {
        const string ticket = """[{"type":"reasoning.encrypted","data":"B"}]""";
        ChatMessage owned = WithTicket(ticket);
        List<ChatMessage> history = [new(ChatRole.User, "q"), owned];

        ReplayPlan plan = ReplayPlan.For(history, new ReasoningGate("provider-b", "model-b"));

        Assert.Null(plan.Tickets);
        // 历史里的原票一个字节不动
        Assert.Equal(ticket, owned.Contents.OfType<TextReasoningContent>().Single().ProtectedData);
        // 发出去的那份：密文摘掉后正文也是空的，整条思考内容都不留——
        // 各协议都不收空思考块（responses 侧是没有 encrypted_content 的 reasoning item，
        // Anthropic 侧是空签名的 thinking 块，后者直接被服务端拒）
        Assert.Empty(plan.Messages[1].Contents.OfType<TextReasoningContent>());
    }

    /// <summary>
    /// 计划走过的就是它发出去的那份：建好之后往原列表里插/删/改，都不影响它。
    ///
    /// 历史是可变集合（消息支持随时增删改），而回填靠序号对齐。增删会让条数变化，
    /// 改写器的数量闸门兜得住；<b>改一条</b>（重新生成、编辑）条数不变、内容换了，
    /// 闸门拦不住——那种情况下思考会静默绑到别的消息上。冻结是唯一能挡住它的东西。
    /// </summary>
    [Fact]
    public void Plan_IsFrozenAgainstLaterEdits()
    {
        List<ChatMessage> history =
        [
            new(ChatRole.Assistant, [new TextReasoningContent("第一条")]),
        ];

        ReplayPlan plan = ReplayPlan.For(history, Current);

        // 建好之后：前面插一条、后面删掉原那条、再换一条内容
        history.Insert(0, new ChatMessage(ChatRole.User, "插进来的"));
        history.RemoveAt(1);
        history.Add(new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("后来的")]));

        Assert.Single(plan.Messages);
        Assert.Equal("第一条", plan.Reasoning![0]);
        Assert.Single(plan.Reasoning);
    }

    /// <summary>什么都没有时两条清单都是 null——改写整段跳过</summary>
    [Fact]
    public void NothingToReplay_ReturnsNullLists()
    {
        ReplayPlan plan = ReplayPlan.For([new ChatMessage(ChatRole.User, "q")], Current);

        Assert.Null(plan.Reasoning);
        Assert.Null(plan.Tickets);
    }
}
