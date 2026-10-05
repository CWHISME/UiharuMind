using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using UiharuMind.Core.AI.Models;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace HttpAllocBench;

/// <summary>
/// 造一次调用的 SSE 响应。块形态照真实网关帧（2026-10 日志里的收尾帧）：带 request_id，
/// 网关风格每块 finish_reason 为空串，商汤自研模型的思考在 reasoning 字段
/// </summary>
internal static class SseData
{
    private const string Id = "00effe4a336e482aafe657de4133e634";

    private static readonly string[] Pieces =
        ["用户", "让我", "看看", "这个", "问题", "，", "首先", "需要", "确认", "代码", "里的", "the", " file", " path", "。", "然后"];

    /// <summary>思考 → 正文 → 一个工具调用（参数分块流出）→ 收尾帧 → 用量帧 → [DONE]</summary>
    public static byte[] Build(string model, bool sensenovaReasoning, string midFinishReason, int reasoning, int content,
        int toolArgs)
    {
        var sb = new StringBuilder();
        var rng = new Random(42);
        void Event(string json) => sb.Append("data: ").Append(json).Append("\n\n");
        string Piece() => Pieces[rng.Next(Pieces.Length)] + Pieces[rng.Next(Pieces.Length)];

        Event(Chunk(model, "\"role\":\"assistant\",\"content\":\"\"", midFinishReason));
        for (int i = 0; i < reasoning; i++)
        {
            Event(Chunk(model, sensenovaReasoning
                ? $"\"content\":\"\",\"reasoning\":\"{Piece()}\""
                : $"\"content\":\"\",\"reasoning_content\":\"{Piece()}\"", midFinishReason));
        }

        for (int i = 0; i < content; i++) Event(Chunk(model, $"\"content\":\"{Piece()}\"", midFinishReason));
        if (toolArgs > 0)
        {
            Event(Chunk(model,
                "\"tool_calls\":[{\"index\":0,\"id\":\"call_0001\",\"type\":\"function\",\"function\":{\"name\":\"Write\",\"arguments\":\"\"}}]",
                midFinishReason));
            for (int i = 0; i < toolArgs; i++)
            {
                string arguments = i == 0 ? "{\\\"path\\\":\\\"a.md\\\",\\\"content\\\":\\\"" : Piece();
                Event(Chunk(model, $"\"tool_calls\":[{{\"index\":0,\"function\":{{\"arguments\":\"{arguments}\"}}}}]",
                    midFinishReason));
            }
        }

        Event(Chunk(model, "", toolArgs > 0 ? "\"tool_calls\"" : "\"stop\""));
        Event($"{{\"id\":\"{Id}\",\"created\":1791193729,\"model\":\"{model}\",\"object\":\"chat.completion.chunk\",\"choices\":[]," +
              "\"usage\":{\"prompt_tokens\":84209,\"completion_tokens\":2544,\"total_tokens\":86753," +
              $"\"completion_tokens_details\":{{\"reasoning_tokens\":2281}},\"prompt_tokens_details\":{{\"cached_tokens\":82944}}}},\"request_id\":\"{Id}\"}}");
        sb.Append("data: [DONE]\n\n");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string Chunk(string model, string delta, string finishReason) =>
        $"{{\"id\":\"{Id}\",\"created\":1791193729,\"model\":\"{model}\",\"object\":\"chat.completion.chunk\"," +
        $"\"choices\":[{{\"index\":0,\"delta\":{{{delta}}},\"finish_reason\":{finishReason}}}],\"request_id\":\"{Id}\"}}";
}

/// <summary>造一份长跑量级的会话：系统提示 + 多轮「思考 + 工具调用 + 工具结果」+ 25 个工具定义</summary>
internal static class Conversation
{
    private const string Paragraph =
        "群聊里每个成员各自读代码、跑回归、提交，彼此互审。这一段是为了把请求体撑到真实长跑的量级：" +
        "提示词、角色卡、工作区规矩、历史发言与工具结果都在里面。The quick brown fox jumps over the lazy dog. ";

    /// <param name="rounds">轮数；95 轮约 37.5 万字符 / 458KB</param>
    /// <param name="withImage">末尾带一张 256KB 的图（看 base64 抹不抹得掉）</param>
    /// <param name="thinkingChars">每轮思考的字数</param>
    public static (List<ChatMessage> Messages, ChatOptions Options, Dictionary<string, string> ReasoningByCallId) Build(
        int rounds, bool withImage, int thinkingChars = 240)
    {
        var rng = new Random(7);
        List<ChatMessage> messages = [new(ChatRole.System, string.Concat(Enumerable.Repeat(Paragraph, 120)))];
        Dictionary<string, string> reasoning = new(StringComparer.Ordinal);
        for (int round = 0; round < rounds; round++)
        {
            messages.Add(new ChatMessage(ChatRole.User, "[黑猫]: " + string.Concat(Enumerable.Repeat(Paragraph, 2))));
            string callId = $"call_{round:0000}";
            string think = string.Concat(Enumerable.Repeat("先读一下文件再决定怎么改。", thinkingChars / 12));
            reasoning[callId] = think;
            messages.Add(new ChatMessage(ChatRole.Assistant,
            [
                new TextReasoningContent(think),
                new TextContent("我先读一下。"),
                new FunctionCallContent(callId, "Read",
                    new Dictionary<string, object?> { ["path"] = $"src/File{round}.cs", ["offset"] = 0 }),
            ]));
            messages.Add(new ChatMessage(ChatRole.Tool,
            [
                new FunctionResultContent(callId,
                    string.Concat(Enumerable.Repeat("    public void Method() { /* 代码行 */ }\n", 60 + rng.Next(40)))),
            ]));
        }

        if (withImage)
        {
            byte[] png = new byte[256 * 1024];
            rng.NextBytes(png);
            messages.Add(new ChatMessage(ChatRole.User, [new TextContent("看图"), new DataContent(png, "image/png")]));
        }

        var options = new ChatOptions
        {
            Tools = Enumerable.Range(0, 25).Select(i => (AITool)AIFunctionFactory.Create(
                (string path, int offset, int limit, string? pattern) => "",
                $"Tool{i}", "读取文件、搜索代码或执行命令的工具。参数说明：path 是相对工作区的路径，offset 与 limit 控制读取范围，pattern 为可选的正则。")).ToList(),
            Temperature = 0.7f,
        };
        return (messages, options, reasoning);
    }
}

/// <summary>远程模型：总带 max_tokens（与真实远程配置一致）；deepSeek 时要求回填思考</summary>
internal sealed class FakeModel(bool deepSeek) : ILlmModel
{
    public string ModelName => "bench";
    public string ModelPath => "http://localhost";
    public bool IsVision => false;
    public string ModelDescription => "";
    public string ModelId => deepSeek ? "deepseek-v4-flash" : "sensenova-6.8-flash-lite";
    public int Port => 0;
    public bool RequiresReasoningContentRoundtrip => deepSeek;
    public bool OmitSamplingParams => false;

    public IReadOnlyList<KeyValuePair<string, JsonNode?>>? GetExtraParams() =>
        [new("max_tokens", JsonValue.Create(393216))];
}

/// <summary>假服务端：读走请求体（可留一份），回预先造好的 SSE</summary>
internal sealed class CaptureHandler(byte[] sse, bool capture = false) : HttpMessageHandler
{
    public string? LastBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Content != null)
        {
            if (capture) LastBody = await request.Content.ReadAsStringAsync(ct);
            else await request.Content.CopyToAsync(Stream.Null, ct);
        }

        var content = new ByteArrayContent(sse);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}

internal static class Clients
{
    public static async Task ConsumeAsync(IChatClient client, IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        await foreach (var _ in client.GetStreamingResponseAsync(messages, options))
        {
        }
    }
}
