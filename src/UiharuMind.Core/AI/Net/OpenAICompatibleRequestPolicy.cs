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
using System.Text;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Net;

/// <summary>
/// 请求体在管道里<b>每次调用只改写、记录一次</b>（挂在重试策略之前，<see cref="PipelinePosition.PerCall"/>）。
///
/// 原先这件事做在 HTTP 层，而 SDK 的重试发生在 HTTP 层之上：每撞一次 429 就把整份请求体重读、解析、改写、
/// 记日志一遍。并行群聊里四十多万字符的请求体约 7MB/次，一轮长跑的限流重试合计约 1GB 分配，
/// <c>Bodies.txt</c> 十几分钟就写满一代。改写结果只取决于请求体与调用上下文，重试时都不变，做一次即可。
/// </summary>
internal sealed class OpenAICompatibleRequestPolicy : PipelinePolicy
{
    private readonly Func<string, string> _prepare; //改写并记录，返回要发出去的正文

    /// <summary>
    /// 按模型改写请求体并记日志
    /// </summary>
    /// <param name="model">目标模型</param>
    public OpenAICompatibleRequestPolicy(ILlmModel? model) : this(json =>
    {
        string rewritten = OpenAICompatibleRequestRewriter.Rewrite(json, model);
        LogRequest(rewritten);
        return rewritten;
    })
    {
    }

    /// <summary>
    /// 自定义请求体的处理（测试用）
    /// </summary>
    /// <param name="prepare">收到 SDK 序列化出的正文，返回要发出去的正文</param>
    internal OpenAICompatibleRequestPolicy(Func<string, string> prepare)
    {
        _prepare = prepare;
    }

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        if (TakeBody(message) is { } body)
        {
            using MemoryStream stream = new();
            body.WriteTo(stream, message.CancellationToken);
            Replace(message, body, stream);
        }

        ProcessNext(message, pipeline, currentIndex);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        if (TakeBody(message) is { } body)
        {
            using MemoryStream stream = new();
            await body.WriteToAsync(stream, message.CancellationToken).ConfigureAwait(false);
            Replace(message, body, stream);
        }

        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
    }

    private static BinaryContent? TakeBody(PipelineMessage message) =>
        string.Equals(message.Request.Method, "POST", StringComparison.OrdinalIgnoreCase) ? message.Request.Content : null;

    private void Replace(PipelineMessage message, BinaryContent body, MemoryStream stream)
    {
        string json = Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
        message.Request.Content = BinaryContent.Create(BinaryData.FromString(_prepare(json)));
        // 换下来的这份要自己释放：Content 的 setter 只赋值不释放，而流式调用里 SDK 自己那次 Dispose 发生在序列化之前，
        // 上面 WriteTo 时现租的池化缓冲段（16KB 一段）原本只能等 message 释放时还，换掉之后就永远还不回去了
        body.Dispose();
    }

    /// <summary>
    /// 请求体日志。<b>完全不截断</b>——提示词、工具定义与参数都要能完整看到。
    /// 超过阈值的正文由日志层外置到 <c>Bodies.txt</c>，面板只吃索引。
    /// 仍然抹 base64：那不是截断，是把毫无阅读价值的附件载荷（一张图就十几 MB）换成一句体量说明。
    /// </summary>
    private static void LogRequest(string content)
    {
        Log.Debug($"OpenAI-compatible request ({content.Length:N0} chars): {LlmBodyLogFormat.ForLog(content)}",
            ELogCategory.LlmRequest);
    }
}
