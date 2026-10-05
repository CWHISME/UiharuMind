using System.Buffers;
using System.ClientModel;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using HttpAllocBench.Proto;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.Core.LLM;
using UiharuMind.Core.Core.SimpleLog;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace HttpAllocBench;

/// <summary>请求侧各环节（每次调用一次）：现状对 P2 原型，请求体取 SDK 实际序列化出的那份</summary>
internal static class RequestBench
{
    public static async Task Run(AllocListener alloc, string home)
    {
        Console.WriteLine("== 请求侧（长跑量级请求体，每次调用一次）==");
        var server = new CaptureHandler(SseData.Build("m", false, "null", 1, 1, 0), capture: true);
        IChatClient client = OpenAICompatibleChatClient.Create(
            new OpenAICompatibleHttpHandler("http://localhost/v1", server), model: null, "deepseek-v4-flash", "key");
        var (messages, options, reasoning) = Conversation.Build(rounds: 95, withImage: false);
        await Clients.ConsumeAsync(client, messages, options);

        string body = server.LastBody!;
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        Console.WriteLine($"  SDK 请求体 {body.Length:N0} 字符 / {Measure.Kb(bodyBytes.Length)}，" +
                          $"\\uXXXX 转义 {CountOf(body, "\\u"):N0} 处，原样中文 {body.Count(c => c is >= '一' and <= '鿿'):N0} 字");

        // 改写：现状 DOM 对拼接原型
        foreach (bool deepSeek in new[] { false, true })
        {
            var model = new FakeModel(deepSeek);
            LlmRequestContext.PendingReasoningByCallId = reasoning;
            string dom = OpenAICompatibleRequestRewriter.Rewrite(body, model);
            byte[] spliced = SpliceRewrite(bodyBytes, model, reasoning)!;
            bool same = JsonNode.DeepEquals(JsonNode.Parse(dom), JsonNode.Parse(spliced));
            var current = Measure.Run(alloc, 20, () => OpenAICompatibleRequestRewriter.Rewrite(body, model));
            var proto = Measure.Run(alloc, 20, () => SpliceRewrite(bodyBytes, model, reasoning));
            LlmRequestContext.PendingReasoningByCallId = null;
            Console.WriteLine($"  改写[{(deepSeek ? "DeepSeek：max_tokens + 回填思考" : "只加 max_tokens")}]  语义一致 {same}");
            Console.WriteLine($"    现状 DOM：{Measure.Kb(current.Bytes)}（大对象堆约 {Measure.Kb(current.LohBytes)}），{current.Micros / 1000:0.00} ms，发出 {Measure.Kb(Encoding.UTF8.GetByteCount(dom))}");
            Console.WriteLine($"    P2 拼接：{Measure.Kb(proto.Bytes)}（大对象堆约 {Measure.Kb(proto.LohBytes)}），{proto.Micros / 1000:0.00} ms，发出 {Measure.Kb(spliced.Length)}");
        }

        // 请求体日志：现状 = ForLog + 插值（与 OpenAICompatibleRequestPolicy.LogRequest 同形）；原型 = 字节进字节出
        string rewritten = OpenAICompatibleRequestRewriter.Rewrite(body, new FakeModel(false));
        byte[] rewrittenBytes = Encoding.UTF8.GetBytes(rewritten);
        string forLog = LlmBodyLogFormat.ForLog(rewritten);
        var logBuffer = new PooledByteWriter(64 * 1024);
        int protoLogLength = Utf8BodyLogFormat.Format(rewrittenBytes, logBuffer);
        string protoLog = Encoding.UTF8.GetString(logBuffer.WrittenSpan[..protoLogLength]);
        var forLogCost = Measure.Run(alloc, 20, () =>
            GC.KeepAlive($"OpenAI-compatible request ({rewritten.Length:N0} chars): {LlmBodyLogFormat.ForLog(rewritten)}"));
        var protoLogCost = Measure.Run(alloc, 20, () =>
        {
            logBuffer.Clear();
            Utf8BodyLogFormat.Format(rewrittenBytes, logBuffer);
        });
        Console.WriteLine($"  请求体日志格式化  与 ForLog 逐字相同 {forLog == protoLog}");
        Console.WriteLine($"    现状 ForLog：{Measure.Kb(forLogCost.Bytes)}（大对象堆约 {Measure.Kb(forLogCost.LohBytes)}），{forLogCost.Micros / 1000:0.00} ms");
        Console.WriteLine($"    P2 字节版：{Measure.Kb(protoLogCost.Bytes)}（大对象堆约 {Measure.Kb(protoLogCost.LohBytes)}），{protoLogCost.Micros / 1000:0.00} ms");

        // 策略搬运：现状 MemoryStream 逐次翻倍 + GetString + FromString；原型按长度从池里租整块。
        // 走 WriteToAsync 与生产的 ProcessAsync 一致——BinaryData 做底的正文同步 WriteTo 会先 ToArray 整份复制一遍
        BinaryContent sdkBody = BinaryContent.Create(BinaryData.FromBytes(bodyBytes));
        var shuttle = Measure.Run(alloc, 20, () =>
        {
            using MemoryStream stream = new();
            sdkBody.WriteToAsync(stream).GetAwaiter().GetResult();
            string json = Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
            GC.KeepAlive(BinaryContent.Create(BinaryData.FromString(json)));
        });
        var protoShuttle = Measure.Run(alloc, 20, () =>
        {
            sdkBody.TryComputeLength(out long length);
            byte[] rented = ArrayPool<byte>.Shared.Rent((int)length);
            sdkBody.WriteToAsync(new MemoryStream(rented, 0, (int)length, writable: true)).GetAwaiter().GetResult();
            ArrayPool<byte>.Shared.Return(rented);
        });
        Console.WriteLine($"  策略搬运  现状：{Measure.Kb(shuttle.Bytes)}（大对象堆约 {Measure.Kb(shuttle.LohBytes)}）；P2 池化：{Measure.Kb(protoShuttle.Bytes)}");

        // Bodies 落盘（现状 LogStore，本轮已改为编码进池化缓冲）
        using (var store = new LogStore(Path.Combine(home, "LogStoreBench")))
        {
            var item = new LogItem(ELogType.Log, $"OpenAI-compatible request ({rewritten.Length:N0} chars): {forLog}",
                ELogCategory.LlmRequest);
            var append = Measure.Run(alloc, 20, () => store.Append(item));
            Console.WriteLine($"  Bodies 落盘 {item.Text.Length:N0} 字符：{Measure.Kb(append.Bytes)}/条，{append.Micros / 1000:0.00} ms");
        }

        // 思考回填的收集：LazyChatClient 每次请求都做，只有要求回填思考的模型用得上
        MethodInfo collect = typeof(LazyChatClient).GetMethod("CollectReasoningByCallId", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (int thinkingChars in new[] { 240, 3000 })
        {
            IReadOnlyList<ChatMessage> history = Conversation.Build(rounds: 95, withImage: false, thinkingChars).Messages;
            var collectCost = Measure.Run(alloc, 50, () => collect.Invoke(null, [history]));
            Console.WriteLine($"  思考回填收集（95 轮，每轮思考 {thinkingChars} 字）：{Measure.Kb(collectCost.Bytes)}/次请求");
        }

        // 带图：base64 抹得掉吗
        var (imageMessages, imageOptions, _) = Conversation.Build(rounds: 2, withImage: true);
        await Clients.ConsumeAsync(client, imageMessages, imageOptions);
        string imageBody = server.LastBody!;
        var imageBuffer = new PooledByteWriter(64 * 1024);
        int imageLength = Utf8BodyLogFormat.Format(Encoding.UTF8.GetBytes(imageBody), imageBuffer);
        Console.WriteLine($"  带一张 256KB 图：ForLog 抹掉 base64 {LlmBodyLogFormat.ForLog(imageBody).Contains("base64 chars>")}，" +
                          $"P2 字节版抹掉 {Encoding.UTF8.GetString(imageBuffer.WrittenSpan[..imageLength]).Contains("base64 chars>")}");
        Console.WriteLine();
    }

    private static byte[]? SpliceRewrite(byte[] json, FakeModel model, IReadOnlyDictionary<string, string> reasoning) =>
        Utf8RequestRewriter.Rewrite(json, model.GetExtraParams(), forbidToolCalls: false,
            model.RequiresReasoningContentRoundtrip ? reasoning : null, model.OmitSamplingParams);

    private static int CountOf(string text, string value)
    {
        int count = 0;
        for (int i = text.IndexOf(value, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal)) count++;
        return count;
    }
}
