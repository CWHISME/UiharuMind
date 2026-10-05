using System.Buffers;
using System.Text;
using UiharuMind.Core.AI.Net;

namespace HttpAllocBench;

/// <summary>响应侧：SSE 清洗流每次调用、修正器每块的分配（现状实现，回归基线）</summary>
internal static class SseBench
{
    public static void Run(AllocListener alloc)
    {
        Console.WriteLine("== 响应侧：SSE 清洗（一次调用 = 1500 思考块 + 300 正文块 + 200 工具参数块）==");
        (string Name, byte[] Sse)[] cases =
        [
            ("网关风格 finish_reason=\"\"", SseData.Build("deepseek-v4-flash", false, "\"\"", 1500, 300, 200)),
            ("商汤 reasoning 字段", SseData.Build("sensenova-6.8-flash-lite", true, "\"\"", 1500, 300, 200)),
            ("标准 finish_reason=null", SseData.Build("deepseek-v4-flash", false, "null", 1500, 300, 200)),
        ];

        byte[] buffer = new byte[4096];
        foreach (var (name, sse) in cases)
        {
            int chunks = Encoding.UTF8.GetString(sse).Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Length;
            var cost = Measure.Run(alloc, 30, () =>
            {
                using var stream = new SseSanitizingStream(new MemoryStream(sse));
                while (stream.ReadAsync(buffer.AsMemory()).AsTask().GetAwaiter().GetResult() > 0)
                {
                }
            });
            Console.WriteLine($"  [{name}] {chunks} 块：{Measure.Kb(cost.Bytes)}/次调用（{cost.Bytes / chunks:0} B/块），{cost.Micros / 1000:0.00} ms");
        }

        Console.WriteLine("== 单块修正（TryFixEventStreamLine，输出缓冲复用）==");
        (string Label, string Line)[] lines =
        [
            ("思考块 finish_reason=\"\"", Line(SseData.Build("deepseek-v4-flash", false, "\"\"", 1, 0, 0), 1)),
            ("商汤 reasoning 改名", Line(SseData.Build("sensenova-6.8-flash-lite", true, "\"\"", 1, 0, 0), 1)),
            ("工具参数块", Line(SseData.Build("deepseek-v4-flash", false, "\"\"", 0, 0, 2), 3)),
            ("正文块 null（无需修）", Line(SseData.Build("deepseek-v4-flash", false, "null", 0, 1, 0), 1)),
        ];
        var output = new ArrayBufferWriter<byte>(4096);
        foreach (var (label, line) in lines)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(line);
            var cost = Measure.Run(alloc, 20000, () =>
            {
                output.ResetWrittenCount();
                OpenAiCompatibleResponseFixer.TryFixEventStreamLine(bytes, output);
            });
            Console.WriteLine($"  {label,-24} {cost.Bytes,5:0} B/块，{cost.Micros,5:0.00} µs");
        }

        Console.WriteLine();
    }

    private static string Line(byte[] sse, int index) =>
        Encoding.UTF8.GetString(sse).Split("\n\n", StringSplitOptions.RemoveEmptyEntries)[index];
}
