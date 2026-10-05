using System.ClientModel.Primitives;
using System.Diagnostics;
using HttpAllocBench.Proto;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.Core.LLM;
using UiharuMind.Core.Core.SimpleLog;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace HttpAllocBench;

/// <summary>
/// 端到端一次流式调用（MEAI → SDK → 我们这层 → 假服务端），以及连跑时的 GC 行为。
/// 两侧响应都用现状实现，只换请求策略：现状 对 P2 原型
/// </summary>
internal static class EndToEndBench
{
    public static async Task Run(AllocListener alloc, string home)
    {
        Console.WriteLine("== 端到端一次流式调用（网关风格 2005 块，DeepSeek 模型）==");
        byte[] sse = SseData.Build("deepseek-v4-flash", false, "\"\"", 1500, 300, 200);
        var (bigMessages, bigOptions, reasoning) = Conversation.Build(rounds: 95, withImage: false);
        List<ChatMessage> small = [new(ChatRole.User, "你好")];
        var model = new FakeModel(deepSeek: true);

        foreach (var (label, messages, options) in new[]
                 {
                     ("小请求", small, (ChatOptions?)null),
                     ("长跑量级请求", bigMessages, bigOptions),
                 })
        {
            foreach (string variant in new[] { "current", "proto" })
            {
                IChatClient client = CreateClient(variant, sse, model, home);
                LlmRequestContext.PendingReasoningByCallId = reasoning;
                var cost = Measure.Run(alloc, 12, () => Clients.ConsumeAsync(client, messages, options).GetAwaiter().GetResult());
                LlmRequestContext.PendingReasoningByCallId = null;
                Console.WriteLine($"  [{label}] {(variant == "proto" ? "P2 原型" : "现状"),-6} {Measure.Kb(cost.Bytes),10}/次（大对象堆约 {Measure.Kb(cost.LohBytes)}），{cost.Micros / 1000:0.0} ms");
            }
        }

        Console.WriteLine();
    }

    /// <summary>带约 100MB 常驻对象（模拟会话数据）连跑 120 次长跑量级调用，数各代回收次数与已提交、碎片、大对象堆峰值</summary>
    public static async Task RunGc(string variant, string home)
    {
        var ballast = new List<string>(1_000_000);
        for (int i = 0; i < 1_000_000; i++) ballast.Add(new string((char)('a' + i % 26), 40));
        byte[] sse = SseData.Build("deepseek-v4-flash", false, "\"\"", 1500, 300, 200);
        var (messages, options, reasoning) = Conversation.Build(rounds: 95, withImage: false);
        IChatClient client = CreateClient(variant, sse, new FakeModel(deepSeek: true), home);

        LlmRequestContext.PendingReasoningByCallId = reasoning;
        for (int i = 0; i < 5; i++) await Clients.ConsumeAsync(client, messages, options);
        GC.Collect();
        int gen0 = GC.CollectionCount(0);
        int gen1 = GC.CollectionCount(1);
        int gen2 = GC.CollectionCount(2);
        TimeSpan pause = GC.GetTotalPauseDuration();
        long maxCommitted = 0;
        long maxFragmented = 0;
        long maxLoh = 0;
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 120; i++)
        {
            await Clients.ConsumeAsync(client, messages, options);
            GCMemoryInfo info = GC.GetGCMemoryInfo(GCKind.Any);
            maxCommitted = Math.Max(maxCommitted, info.TotalCommittedBytes);
            maxFragmented = Math.Max(maxFragmented, info.FragmentedBytes);
            GCGenerationInfo loh = info.GenerationInfo[3];
            maxLoh = Math.Max(maxLoh, loh.SizeAfterBytes + loh.FragmentationAfterBytes);
        }

        LlmRequestContext.PendingReasoningByCallId = null;
        LogManager.Instance.Flush();
        Console.WriteLine($"  [{variant}] 120 次调用 {watch.ElapsedMilliseconds} ms：gen0 {GC.CollectionCount(0) - gen0} 次，" +
                          $"gen1 {GC.CollectionCount(1) - gen1} 次，gen2 {GC.CollectionCount(2) - gen2} 次，" +
                          $"GC 暂停合计 {(GC.GetTotalPauseDuration() - pause).TotalMilliseconds:0} ms；" +
                          $"已提交峰值 {maxCommitted / 1048576} MB，碎片峰值 {maxFragmented / 1048576} MB，大对象堆峰值 {maxLoh / 1048576} MB");
        GC.KeepAlive(ballast);
    }

    private static IChatClient CreateClient(string variant, byte[] sse, FakeModel model, string home)
    {
        var handler = new OpenAICompatibleHttpHandler("http://localhost/v1", new CaptureHandler(sse));
        PipelinePolicy policy = variant == "proto"
            ? new ProtoRequestPolicy(model, Path.Combine(home, "ProtoBodies.txt"))
            : new OpenAICompatibleRequestPolicy(model);
        return Clients.Create(handler, policy);
    }
}
