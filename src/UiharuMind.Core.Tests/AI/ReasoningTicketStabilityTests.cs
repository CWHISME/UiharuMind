using System.ClientModel.Primitives;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Net.Request;
using UiharuMind.Core.AI.Reasoning;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 端到端：票据从响应切下 → <b>挂到消息上</b> → 随消息落盘 → 下一发原样挂回 →
/// 历史部分逐字节相同。
///
/// 这是缓存前缀稳定性的直接检验。上一版实现在这里挂不住：
/// 票只挂最后一条、用一次即消费，于是上一轮挂过票的位置在下一轮变成无票形态，
/// 那一条的字节变了 → 前缀分叉（实测命中率从 88.2% 掉到 35.3%）。
///
/// <para>ADR 0101 之后「挂回哪条消息」不再是序号对位，而是<b>绑在消息对象上</b>：
/// 这里走的正是生产路径的那两步——门禁过滤、再按消息绑定，然后交给 SDK 自己的序列化器。</para>
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
        Assert.Equal(ExtractHistory(second, 2), ExtractHistory(third, 2));
        Assert.NotEqual(first, second);
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

        JsonArray messages = Messages(Rewrite(history));

        Assert.Equal(TicketA, messages[1]!["reasoning_details"]!.ToJsonString());
        Assert.Null(messages[2]!["reasoning_details"]); //第 2 条没票
    }

    /// <summary>换模型后整条丢弃：旧票不带出去，但票本身留着（换回来还能用）</summary>
    [Fact]
    public void TicketFromAnotherProvider_IsNotReplayed()
    {
        List<ChatMessage> history = [new(ChatRole.User, "u1"), AssistantWithTicket("a1", TicketA)];

        //产票方与请求方不同 —— 门禁不过
        JsonArray messages = Messages(Rewrite(history, current: "provider-b"));

        Assert.Null(messages[1]!["reasoning_details"]);
    }

    /// <summary>
    /// 门禁按 provider + model 双等：同供应商不同模型也不行。
    /// </summary>
    [Fact]
    public void TicketFromAnotherModel_IsNotReplayed()
    {
        List<ChatMessage> history = [new(ChatRole.User, "u1"), AssistantWithTicket("a1", TicketA)];

        JsonArray messages = Messages(Rewrite(history, currentModel: "model-b"));

        Assert.Null(messages[1]!["reasoning_details"]);
    }

    /// <summary>
    /// 生产路径那两步：门禁过滤 → 按消息绑定 → SDK 序列化。
    ///
    /// 与 <see cref="ReasoningReplayChatClient"/> 做的是同一件事；这里不经过它，
    /// 是因为要比的是「序列化出来的字节」，而不是客户端有没有被调用。
    /// </summary>
    private static string Rewrite(IReadOnlyList<ChatMessage> history,
        string current = "provider-a", string currentModel = "model-a")
    {
        IReadOnlyList<ChatMessage> gated = ReasoningGateFilter.Apply(history, new ReasoningGate(current, currentModel));

        JsonArray messages = [];
        foreach (ChatMessage message in gated)
        {
            (string? text, string? ticket) = ReasoningReplay.Read(message);
            ChatMessage? bound = text is null && ticket is null
                ? null
                : ChatCompletionMessageBinder.Attach(message, text, ticket, ReasoningReplayPolicy.DefaultField);
            messages.Add(WireNode(bound ?? message));
        }

        return new JsonObject { ["messages"] = messages }.ToJsonString();
    }

    /// <summary>挂好思考的那份走 SDK 自己的序列化器；其余的按最小形状落成 JSON</summary>
    private static JsonNode WireNode(ChatMessage message) =>
        message.RawRepresentation is not null
            ? JsonNode.Parse(ModelReaderWriter.Write(message.RawRepresentation, new ModelReaderWriterOptions("W")).ToString())!
            : new JsonObject
            {
                ["role"] = message.Role == ChatRole.Assistant ? "assistant" : "user",
                ["content"] = message.Text,
            };

    private static JsonArray Messages(string body) => (JsonArray)JsonNode.Parse(body)!["messages"]!;

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

    /// <summary>取出请求体里前 count 条消息的规范化文本</summary>
    private static string ExtractHistory(string body, int count)
    {
        JsonArray messages = Messages(body);
        JsonArray head = [];
        for (int i = 0; i < count; i++) head.Add(messages[i]!.DeepClone());
        return head.ToJsonString();
    }
}
