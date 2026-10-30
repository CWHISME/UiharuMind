using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Net.Request;
using UiharuMind.Core.AI.Reasoning;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 端到端：票据从响应切下 → 按序号存 → 下一发原样挂回 → <b>历史部分逐字节相同</b>。
///
/// 这是缓存前缀稳定性的直接检验。上一版实现在这里挂不住：
/// 票只挂最后一条、用一次即消费，于是上一轮挂过票的位置在下一轮变成无票形态，
/// 那一条的字节变了 → 前缀分叉（实测命中率从 88.2% 掉到 35.3%）。
///
/// 这里不测单个零件，只测「连续三轮之后，历史部分是否还是一模一样」。
/// </summary>
public class ReasoningTicketStabilityTests
{
    private const string TicketA = """[{"type":"reasoning.encrypted","data":"AAA","id":"rs_1:rs_a","format":"meta-responses-v1","index":0}]""";
    private const string TicketB = """[{"type":"reasoning.encrypted","data":"BBB","id":"rs_2:rs_b","format":"meta-responses-v1","index":0}]""";

    /// <summary>
    /// 三轮对话，第三轮请求体里的历史部分必须与第二轮逐字节相同。
    ///
    /// 「历史部分」= 除末尾新增消息之外的所有消息。服务端的前缀缓存按这个算，
    /// 差一个字节就整段冷掉。
    /// </summary>
    [Fact]
    public void HistoryBytes_StayIdenticalAcrossRounds()
    {
        //第 1 轮：只有用户消息，模型回了带票的助手消息
        List<ChatMessage> history = [new(ChatRole.User, "u1")];
        string first = Rewrite(history, null);

        //票记在第 0 条助手消息上（这一发的回复会落在序号 0）
        ReasoningTicket ticketA = new(TicketA, new("commandcode", "muse"));
        ReasoningTicket[] round1 = [ticketA];
        //第 1 轮的回复落进历史
        history.Add(new(ChatRole.Assistant, "a1"));

        //第 2 轮：带上第 0 条的票
        string second = Rewrite(history, round1);

        //第 2 轮的回复也带票，记在第 1 条上
        history.Add(new(ChatRole.Assistant, "a2"));
        ReasoningTicket ticketB = new(TicketB, new("commandcode", "muse"));
        string third = Rewrite(history, [round1[0], ticketB]);

        //第 2 轮里的历史（消息 0..1）与第 3 轮里的历史（消息 0..1）必须逐字节相同
        string secondHistory = ExtractHistory(second, 2);
        string thirdHistory = ExtractHistory(third, 2);

        Assert.Equal(secondHistory, thirdHistory);
    }

    /// <summary>
    /// 更强的一条：第 1 轮那条消息在第 3 轮里仍然带着<b>同一张</b>票。
    ///
    /// 上一版实现在这里挂不住——票被消费掉后，第 1 轮那条消息在第 3 轮变回无票形态。
    /// </summary>
    [Fact]
    public void TheFirstTicket_IsStillOnItsOwnMessageInTheThirdRound()
    {
        List<ChatMessage> history = [new(ChatRole.User, "u1")];
        history.Add(new(ChatRole.Assistant, "a1"));
        history.Add(new(ChatRole.Assistant, "a2"));

        string rewritten = Rewrite(history, [new ReasoningTicket(TicketA, new("commandcode", "muse"))]);

        JsonArray messages = (JsonArray)JsonNode.Parse(rewritten!)!["messages"]!;

        Assert.Equal(TicketA, messages[1]!["reasoning_details"]!.ToJsonString());
        Assert.Null(messages[2]!["reasoning_details"]); //第 2 条没票
    }

    /// <summary>换模型后整条丢弃：旧票不带出去，但票本身留着（换回来还能用）</summary>
    [Fact]
    public void TicketFromAnotherProvider_IsNotReplayed()
    {
        List<ChatMessage> history = [new(ChatRole.User, "u1"), new(ChatRole.Assistant, "a1")];

        //产票方是 commandcode，请求方是 openrouter —— 门禁不过
        string rewritten = Rewrite(history, [new ReasoningTicket(TicketA, new("commandcode", "muse"))], current: "openrouter");

        JsonArray messages = (JsonArray)JsonNode.Parse(rewritten!)!["messages"]!;

        Assert.Null(messages[1]!["reasoning_details"]);
    }

    /// <summary>
    /// 门禁按 provider + model 双等：同供应商不同模型也不行。
    /// </summary>
    [Fact]
    public void TicketFromAnotherModel_IsNotReplayed()
    {
        List<ChatMessage> history = [new(ChatRole.User, "u1"), new(ChatRole.Assistant, "a1")];

        string rewritten = Rewrite(history, [new ReasoningTicket(TicketA, new("commandcode", "muse"))], currentModel: "deepseek");

        JsonArray messages = (JsonArray)JsonNode.Parse(rewritten!)!["messages"]!;

        Assert.Null(messages[1]!["reasoning_details"]);
    }

    /// <summary>没有票据时请求体一个字节都不该变（不然每次请求都要冷一次前缀缓存）</summary>
    [Fact]
    public void NoTicket_LeavesTheBodyUntouched()
    {
        const string body = """{"messages":[{"role":"assistant","content":"a"}]}""";

        using PooledByteWriter output = new(body.Length);
        bool rewritten = OpenAICompatibleRequestRewriter.Rewrite(Encoding.UTF8.GetBytes(body),
            new RequestRewrite(null, false, null, false), output);

        Assert.False(rewritten); //false 表示「不必改写」，上游会直接发原文
    }

    private static string Rewrite(IReadOnlyList<ChatMessage> history, IReadOnlyList<ReasoningTicket>? tickets,
        string current = "commandcode", string currentModel = "muse")
    {
        IReadOnlyList<string?>? slots = null;
        if (tickets != null)
        {
            string?[] list = new string?[tickets.Count];
            for (int i = 0; i < tickets.Count; i++)
            {
                ReasoningTicket ticket = tickets[i];
                list[i] = ticket.Producer.Allows(current, currentModel) ? ticket.Json : null;
            }

            slots = list;
        }

        string body = BuildBody(history);
        using PooledByteWriter output = new(body.Length);
        return OpenAICompatibleRequestRewriter.Rewrite(Encoding.UTF8.GetBytes(body),
                   new RequestRewrite(null, false, null, false, ReasoningTickets: slots), output)
            ? Encoding.UTF8.GetString(output.WrittenSpan)
            : body;
    }

    private static string BuildBody(IReadOnlyList<ChatMessage> history)
    {
        JsonArray messages = [];
        foreach (ChatMessage message in history)
        {
            messages.Add(new JsonObject
            {
                ["role"] = message.Role == ChatRole.Assistant ? "assistant" : "user",
                ["content"] = message.Text,
            });
        }

        return new JsonObject { ["messages"] = messages }.ToJsonString();
    }

    /// <summary>取出请求体里前 count 条消息的规范化文本</summary>
    private static string ExtractHistory(string body, int count)
    {
        JsonArray messages = (JsonArray)JsonNode.Parse(body)!["messages"]!;
        JsonArray head = [];
        for (int i = 0; i < count; i++) head.Add(messages[i]!.DeepClone());
        return head.ToJsonString();
    }
}
