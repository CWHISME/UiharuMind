/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.ClientModel;
using System.ClientModel.Primitives;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Net;

/// <summary>
/// 处理一份 SDK 序列化出的请求体
/// </summary>
/// <param name="json">SDK 序列化出的请求体</param>
/// <param name="output">要换上去的新正文写到这里</param>
/// <returns>是否写了新正文；false 时照原样发 SDK 那份</returns>
internal delegate bool RequestBodyPreparer(ReadOnlySpan<byte> json, PooledByteWriter output);

/// <summary>
/// 请求体在管道里<b>每次调用只改写、记录一次</b>（挂在重试策略之前，<see cref="PipelinePosition.PerCall"/>）。
///
/// 原先这件事做在 HTTP 层，而 SDK 的重试发生在 HTTP 层之上：每撞一次 429 就把整份请求体重读、解析、改写、
/// 记日志一遍。并行群聊里四十多万字符的请求体约 7MB/次，一轮长跑的限流重试合计约 1GB 分配，
/// <c>Bodies.txt</c> 十几分钟就写满一代。改写结果只取决于请求体与调用上下文，重试时都不变，做一次即可。
///
/// 全程按字节：SDK 正文拷进池化缓冲、改写进池化缓冲、日志按字节格式化入队，发出去的是切段的正文，
/// 一次调用不在大对象堆上留任何东西（忙时 gen2 与碎片主要就来自这里，实测见 docs/perf/2026-10-05）。
/// </summary>
internal sealed class OpenAICompatibleRequestPolicy : PipelinePolicy
{
    private const int UnknownLengthHint = 64 * 1024;

    private readonly RequestBodyPreparer _prepare;

    /// <summary>
    /// 按模型改写请求体并记日志
    /// </summary>
    /// <param name="model">目标模型</param>
    public OpenAICompatibleRequestPolicy(ILlmModel? model) : this((json, output) =>
    {
        bool rewritten = OpenAICompatibleRequestRewriter.Rewrite(json, OpenAICompatibleRequestRewriter.For(model), output);
        LogRequest(rewritten ? output.WrittenSpan : json);
        return rewritten;
    })
    {
    }

    /// <summary>
    /// 自定义请求体的处理（测试用）
    /// </summary>
    /// <param name="prepare">收到 SDK 序列化出的正文，决定要不要换</param>
    internal OpenAICompatibleRequestPolicy(RequestBodyPreparer prepare)
    {
        _prepare = prepare;
    }

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        if (TakeBody(message) is { } body)
        {
            using PooledByteWriter json = Rent(body);
            body.WriteTo(json.AsStream(), message.CancellationToken);
            Replace(message, body, json);
        }

        ProcessNext(message, pipeline, currentIndex);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        if (TakeBody(message) is { } body)
        {
            using PooledByteWriter json = Rent(body);
            await body.WriteToAsync(json.AsStream(), message.CancellationToken).ConfigureAwait(false);
            Replace(message, body, json);
        }

        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
    }

    private static BinaryContent? TakeBody(PipelineMessage message) =>
        string.Equals(message.Request.Method, "POST", StringComparison.OrdinalIgnoreCase) ? message.Request.Content : null;

    private static PooledByteWriter Rent(BinaryContent body) =>
        new(body.TryComputeLength(out long length) && length < int.MaxValue ? (int)length : UnknownLengthHint);

    private void Replace(PipelineMessage message, BinaryContent body, PooledByteWriter json)
    {
        using PooledByteWriter output = new(json.WrittenCount + 4096);
        if (!_prepare(json.WrittenSpan, output)) return; //无需改写就原样用 SDK 那份

        message.Request.Content = new SegmentedBinaryContent(output.WrittenSpan);
        // 换下来的这份要自己释放：Content 的 setter 只赋值不释放，而流式调用里 SDK 自己那次 Dispose 发生在序列化之前，
        // 上面 WriteTo 时现租的池化缓冲段（16KB 一段）原本只能等 message 释放时还，换掉之后就永远还不回去了
        body.Dispose();
    }

    /// <summary>
    /// 请求体日志。<b>完全不截断</b>——提示词、工具定义与参数都要能完整看到。
    /// 超过阈值的正文由日志层外置到 <c>Bodies.txt</c>，面板只吃索引。
    /// 仍然抹 base64：那不是截断，是把毫无阅读价值的附件载荷（一张图就十几 MB）换成一句体量说明。
    /// </summary>
    private static void LogRequest(ReadOnlySpan<byte> body)
    {
        using PooledByteWriter formatted = new(body.Length + body.Length / 4);
        LlmBodyLogFormat.Format(body, formatted);
        Log.Debug($"OpenAI-compatible request ({body.Length:N0} bytes): ", formatted.WrittenSpan, ELogCategory.LlmRequest);
    }
}
