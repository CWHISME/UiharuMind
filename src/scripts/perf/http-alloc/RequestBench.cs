using System.Buffers;
using System.ClientModel;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.Core.LLM;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;


namespace HttpAllocBench;

/// <summary>请求侧各环节（每次调用一次），请求体取 SDK 实际序列化出的那份</summary>
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

        // 改写（写进复用的池化缓冲，与策略里一样）
        var rewriteOutput = new PooledByteWriter(bodyBytes.Length + 4096);
        foreach (bool deepSeek in new[] { false, true })
        {
            var model = new FakeModel(deepSeek);
            var rewrite = new RequestRewrite(model.GetExtraParams(), false, deepSeek ? reasoning : null, false);
            int sent = Rewrite(bodyBytes, rewrite, rewriteOutput).Length;
            var cost = Measure.Run(alloc, 20, () => Rewrite(bodyBytes, rewrite, rewriteOutput));
            Console.WriteLine($"  改写[{(deepSeek ? "DeepSeek：max_tokens + 回填思考" : "只加 max_tokens")}]：" +
                              $"{Measure.Kb(cost.Bytes)}（大对象堆约 {Measure.Kb(cost.LohBytes)}），{cost.Micros / 1000:0.00} ms，发出 {Measure.Kb(sent)}");
        }

        // 请求体日志：格式化 + 入队（与 OpenAICompatibleRequestPolicy.LogRequest 同形）
        byte[] rewrittenBytes = Rewrite(bodyBytes,
            new RequestRewrite(new FakeModel(false).GetExtraParams(), false, null, false), rewriteOutput).ToArray();
        var logCost = Measure.Run(alloc, 20, () =>
        {
            using var output = new PooledByteWriter(rewrittenBytes.Length + rewrittenBytes.Length / 4);
            LlmBodyLogFormat.Format(rewrittenBytes, output);
            Log.Debug($"OpenAI-compatible request ({rewrittenBytes.Length:N0} bytes): ", output.WrittenSpan, ELogCategory.LlmRequest);
        });
        LogManager.Instance.Flush();
        Console.WriteLine($"  请求体日志（格式化 + 入队）：{Measure.Kb(logCost.Bytes)}（大对象堆约 {Measure.Kb(logCost.LohBytes)}），{logCost.Micros / 1000:0.00} ms");

        // 策略搬运：现状 = OpenAICompatibleRequestPolicy 的路子（池化缓冲 + 切段正文）；
        // 走 WriteToAsync 与生产的 ProcessAsync 一致——BinaryData 做底的正文同步 WriteTo 会先 ToArray 整份复制一遍
        BinaryContent sdkBody = BinaryContent.Create(BinaryData.FromBytes(bodyBytes));
        var shuttle = Measure.Run(alloc, 20, () =>
        {
            sdkBody.TryComputeLength(out long length);
            using var json = new PooledByteWriter((int)length);
            sdkBody.WriteToAsync(json.AsStream()).GetAwaiter().GetResult();
            GC.KeepAlive(new SegmentedBinaryContent(json.WrittenSpan));
        });
        Console.WriteLine($"  策略搬运（池化 + 切段正文）：{Measure.Kb(shuttle.Bytes)}（大对象堆约 {Measure.Kb(shuttle.LohBytes)}）");

        // Bodies 落盘（字节正文条目）
        var formatted = new PooledByteWriter(rewrittenBytes.Length * 2);
        LlmBodyLogFormat.Format(rewrittenBytes, formatted);
        using (var store = new LogStore(Path.Combine(home, "LogStoreBench")))
        {
            var item = new LogItem(ELogType.Log, $"OpenAI-compatible request ({rewrittenBytes.Length:N0} bytes): ",
                formatted.WrittenSpan, ELogCategory.LlmRequest);
            var append = Measure.Run(alloc, 20, () => store.Append(item));
            Console.WriteLine($"  Bodies 落盘 {item.CharCount:N0} 字符：{Measure.Kb(append.Bytes)}/条，{append.Micros / 1000:0.00} ms");
        }

        // 思考回填的收集：只有要求回填思考的模型才做（LazyChatClient 挂的是惰性来源）
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
        var imageLog = new PooledByteWriter(64 * 1024);
        LlmBodyLogFormat.Format(Encoding.UTF8.GetBytes(server.LastBody!), imageLog);
        Console.WriteLine($"  带一张 256KB 图：日志里抹掉 base64 {Encoding.UTF8.GetString(imageLog.WrittenSpan).Contains("base64 chars>")}");
        Console.WriteLine();
    }

    private static ReadOnlySpan<byte> Rewrite(byte[] json, RequestRewrite rewrite, PooledByteWriter output)
    {
        output.Rewind(0);
        return OpenAICompatibleRequestRewriter.Rewrite(json, rewrite, output) ? output.WrittenSpan : json;
    }

    private static int CountOf(string text, string value)
    {
        int count = 0;
        for (int i = text.IndexOf(value, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal)) count++;
        return count;
    }
}
