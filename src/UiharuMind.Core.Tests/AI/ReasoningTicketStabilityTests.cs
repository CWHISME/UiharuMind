using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Net.Request;
using UiharuMind.Core.AI.Reasoning;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 端到端：票据从响应切下 → <b>挂到消息上</b> → 随消息落盘 → 下一发原样挂回 →
/// 历史部分逐字节相同。
///
/// 这是缓存前缀稳定性的直接检验。上一版实现在这里挂不住：
/// 票只挂最后一条、用一次即消费，于是上一轮挂过票的位置在下一轮变成无票形态，
/// 那一条的字节变了 → 前缀分叉（实测命中率从 88.2% 掉到 35.3%）。
///
/// ADR 0100 决定 3 之后票据长在消息上，所以这里能测最关键的那条：<b>重启之后</b>（消息重新
/// 从落盘读回来）前缀依然稳定。上一版的内存版在这里必挂——重启即丢票。
/// </summary>
public class ReasoningTicketStabilityTests
{
    private const string TicketA = """[{"type":"reasoning.encrypted","data":"AAA","id":"rs_1:rs_a","format":"probe-v1","index":0}]""";
    private const string TicketB = """[{"type":"reasoning.encrypted","data":"BBB","id":"rs_2:rs_b","format":"probe-v1","index":0}]""";

    private static readonly ReasoningGate Producer = new("provider-a", "model-a");

    /// <summary>造一条挂着票据的助手消息（模拟响应落成消息后带上票据）</summary>
    private static ChatMessage AssistantWithTicket(string text, string? ticketJson)
    {
        if (ticketJson == null) return new(ChatRole.Assistant, text);

        TextReasoningContent reasoning = new(string.Empty);
        ReasoningTicketCarrier.Attach(reasoning, ticketJson, Producer);
        return new ChatMessage(ChatRole.Assistant, [reasoning, new TextContent(text)]);
    }

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
        string first = Rewrite(history);

        //第 1 轮的回复落进历史（带着它自己那张票）
        history.Add(AssistantWithTicket("a1", TicketA));

        //第 2 轮：第 0 条助手消息带票
        string second = Rewrite(history);

        //第 2 轮的回复也带票
        history.Add(AssistantWithTicket("a2", TicketB));

        //第 3 轮：两条助手消息各带各的票
        string third = Rewrite(history);

        //第 2 轮里的历史（消息 0..1）与第 3 轮里的历史（消息 0..1）必须逐字节相同
        string secondHistory = ExtractHistory(second, 2);
        string thirdHistory = ExtractHistory(third, 2);

        Assert.Equal(secondHistory, thirdHistory);
    }

    /// <summary>
    /// <b>跨重启</b>：消息从落盘读回来之后，前缀依然稳定。
    ///
    /// 这是 ADR 0100 决定 3 才做到的。上一版票据只在内存，进程退出即丢——
    /// 重启后历史里挂过票的消息变回无票形态，那一条起前缀全冷
    /// （实测末次会话丢掉 72.1% 的缓存）。
    /// </summary>
    [Fact]
    public void HistoryBytes_StayIdenticalAcrossRestart()
    {
        List<ChatMessage> history = [new(ChatRole.User, "u1")];
        history.Add(AssistantWithTicket("a1", TicketA));

        string before = Rewrite(history);

        //模拟重启：整段历史序列化再读回来（这就是落盘往返）
        List<ChatMessage> reloaded = Reload(history);

        string after = Rewrite(reloaded);

        Assert.Equal(ExtractHistory(before, 2), ExtractHistory(after, 2));
    }

    /// <summary>
    /// 更强的一条：第 1 轮那条消息在第 3 轮里仍然带着<b>同一张</b>票。
    ///
    /// 上一版实现在这里挂不住——票被消费掉后，第 1 轮那条消息在第 3 轮变回无票形态。
    /// </summary>
    [Fact]
    public void TheFirstTicket_IsStillOnItsOwnMessageInTheThirdRound()
    {
        List<ChatMessage> history =
        [
            new(ChatRole.User, "u1"),
            AssistantWithTicket("a1", TicketA),
            AssistantWithTicket("a2", null),
        ];

        string rewritten = Rewrite(history);

        JsonArray messages = (JsonArray)JsonNode.Parse(rewritten!)!["messages"]!;

        Assert.Equal(TicketA, messages[1]!["reasoning_details"]!.ToJsonString());
        Assert.Null(messages[2]!["reasoning_details"]); //第 2 条没票
    }

    /// <summary>换模型后整条丢弃：旧票不带出去，但票本身留着（换回来还能用）</summary>
    [Fact]
    public void TicketFromAnotherProvider_IsNotReplayed()
    {
        List<ChatMessage> history = [new(ChatRole.User, "u1"), AssistantWithTicket("a1", TicketA)];

        //产票方与请求方不同 —— 门禁不过
        string rewritten = Rewrite(history, current: "provider-b");

        JsonArray messages = (JsonArray)JsonNode.Parse(rewritten!)!["messages"]!;

        Assert.Null(messages[1]!["reasoning_details"]);
    }

    /// <summary>
    /// 门禁按 provider + model 双等：同供应商不同模型也不行。
    /// </summary>
    [Fact]
    public void TicketFromAnotherModel_IsNotReplayed()
    {
        List<ChatMessage> history = [new(ChatRole.User, "u1"), AssistantWithTicket("a1", TicketA)];

        string rewritten = Rewrite(history, currentModel: "model-b");

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

    /// <summary>
    /// 按历史里的消息收集票据：位置由消息列表本身保证，不需要旁挂的映射表。
    /// </summary>
    private static IReadOnlyList<string?>? CollectSlots(IReadOnlyList<ChatMessage> history,
        ReasoningGate current)
    {
        List<string?> slots = [];
        foreach (ChatMessage message in history)
        {
            if (message.Role != ChatRole.Assistant)
            {
                continue;
            }

            (string Json, ReasoningGate Producer)? ticket = ReasoningTicketCarrier.Read(message);
            slots.Add(ticket is { } t && t.Producer.Allows(current.Provider, current.Model) ? t.Json : null);
        }

        return slots.Exists(x => x != null) ? slots : null;
    }

    private static string Rewrite(IReadOnlyList<ChatMessage> history,
        string current = "provider-a", string currentModel = "model-a")
    {
        IReadOnlyList<string?>? slots = CollectSlots(history, new ReasoningGate(current, currentModel));

        string body = BuildBody(history);
        using PooledByteWriter output = new(body.Length);
        return OpenAICompatibleRequestRewriter.Rewrite(Encoding.UTF8.GetBytes(body),
                   new RequestRewrite(null, false, null, false, ReasoningTickets: slots), output)
            ? Encoding.UTF8.GetString(output.WrittenSpan)
            : body;
    }

    /// <summary>模拟重启：整段历史序列化再读回来</summary>
    private static List<ChatMessage> Reload(IReadOnlyList<ChatMessage> history)
    {
        List<ChatMessage> reloaded = [];
        foreach (ChatMessage message in history)
        {
            string json = JsonSerializer.Serialize(message, SessionJsonOptions.Default.GetTypeInfo(typeof(ChatMessage)));
            ChatMessage? back = JsonSerializer.Deserialize(json, SessionJsonOptions.Default.GetTypeInfo(typeof(ChatMessage))) as ChatMessage;
            if (back != null) reloaded.Add(back);
        }

        return reloaded;
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
