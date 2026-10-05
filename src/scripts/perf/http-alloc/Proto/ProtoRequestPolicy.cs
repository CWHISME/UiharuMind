using System.Buffers;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Models;

namespace HttpAllocBench.Proto;

/// <summary>
/// P2 原型：请求策略全程按字节。SDK 正文拷进池里租的整块 → 拼接改写进池化缓冲 → 日志按字节格式化、按字节落盘 →
/// 发出去的正文切成 64KB 段（<see cref="SegmentedContent"/>）。无需改写时原样用 SDK 那份、不替换。
/// 日志这里直接追加到文件，模拟「日志加 UTF-8 正文入口、写线程落字节」之后的开销；落地时走 LogManager。
/// </summary>
internal sealed class ProtoRequestPolicy(ILlmModel? model, string bodiesPath) : PipelinePolicy
{
    private static readonly Lock FileLock = new();

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Prepare(message);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        Prepare(message);
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
    }

    private void Prepare(PipelineMessage message)
    {
        if (!string.Equals(message.Request.Method, "POST", StringComparison.OrdinalIgnoreCase) ||
            message.Request.Content is not { } body) return;

        int length = body.TryComputeLength(out long computed) ? (int)computed : 1024 * 1024;
        byte[] input = ArrayPool<byte>.Shared.Rent(length);
        var output = new PooledByteWriter(length + 4096);
        var log = new PooledByteWriter(length + length / 4);
        try
        {
            var stream = new MemoryStream(input, 0, input.Length, writable: true);
            body.WriteTo(stream, message.CancellationToken);
            ReadOnlySpan<byte> json = input.AsSpan(0, (int)stream.Position);

            bool changed = Utf8RequestRewriter.Rewrite(json, model?.GetExtraParams(), LlmRequestContext.ForbidToolCalls,
                model?.RequiresReasoningContentRoundtrip == true ? LlmRequestContext.PendingReasoningByCallId : null,
                model?.OmitSamplingParams == true, output) != null;
            ReadOnlySpan<byte> sent = changed ? output.WrittenSpan : json;

            log.Write(Encoding.UTF8.GetBytes($"OpenAI-compatible request ({sent.Length:N0} bytes): "));
            if (Utf8BodyLogFormat.Format(sent, log) < 0) log.Write(sent);
            log.Write("\n\n"u8);
            lock (FileLock)
            {
                using var file = new FileStream(bodiesPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 1);
                file.Write(log.WrittenSpan);
            }

            if (changed)
            {
                message.Request.Content = new SegmentedContent(sent);
                body.Dispose();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(input);
            output.Dispose();
            log.Dispose();
        }
    }
}

/// <summary>
/// P2 原型：发出去的正文切成 64KB 段（小于 85000 字节，不进大对象堆）。
/// 段归 GC 管、不从池里租：HTTP/2 可能在请求体发完之前就收到响应，那时还池会把别处的数据发出去
/// </summary>
internal sealed class SegmentedContent : BinaryContent
{
    private const int SegmentSize = 64 * 1024;

    private readonly List<byte[]> _segments = [];
    private readonly int _length;

    public SegmentedContent(ReadOnlySpan<byte> data)
    {
        _length = data.Length;
        while (data.Length > 0)
        {
            int size = Math.Min(SegmentSize, data.Length);
            byte[] segment = GC.AllocateUninitializedArray<byte>(size);
            data[..size].CopyTo(segment);
            _segments.Add(segment);
            data = data[size..];
        }
    }

    public override bool TryComputeLength(out long length)
    {
        length = _length;
        return true;
    }

    public override void WriteTo(Stream stream, CancellationToken cancellation)
    {
        foreach (byte[] segment in _segments)
        {
            cancellation.ThrowIfCancellationRequested();
            stream.Write(segment);
        }
    }

    public override async Task WriteToAsync(Stream stream, CancellationToken cancellation)
    {
        foreach (byte[] segment in _segments) await stream.WriteAsync(segment, cancellation).ConfigureAwait(false);
    }

    public override void Dispose()
    {
    }
}
