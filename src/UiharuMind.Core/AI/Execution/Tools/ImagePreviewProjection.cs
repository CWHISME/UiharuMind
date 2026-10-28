using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 把 Read 看图结果里的预览图投影成一条 user 消息，插在那一串 tool 消息之后（ADR 0053）。
/// 只改发出去的那份，历史不动——于是渲染、裁剪、编辑重试、删除闭包都不必认识一种新消息。
/// 哪些调用算看图由 <see cref="ImagePreview.IsImageRead"/> 按参数里的路径判定，不看结果文本。
///
/// 模型自己看不了图时投影照发，只是把图换成占位文本：那几轮助手回复是"看着图"说的，
/// 图整条不发，历史里就只剩它自说自话（见 <see cref="Project"/> 的 <c>attachImages</c>）。
/// </summary>
public static class ImagePreviewProjection
{
    /// <summary>
    /// 投影一次请求的消息
    /// </summary>
    /// <param name="messages">将要发出的消息（历史 + 本轮）</param>
    /// <param name="resolver">解析预览路径（草稿目录简写）</param>
    /// <param name="attachImages">是否真的附图；false 时逐张写成占位文本</param>
    /// <returns>投影后的消息；没有看图结果时原样返回同一实例</returns>
    public static IReadOnlyList<ChatMessage> Project(IReadOnlyList<ChatMessage> messages, AgentPathResolver resolver,
        bool attachImages = true)
    {
        HashSet<string> viewCalls = ViewCallsIn(messages);
        if (viewCalls.Count == 0) return messages;

        List<ChatMessage> projected = new(messages.Count + 1);
        List<string> pending = new();
        for (int i = 0; i < messages.Count; i++)
        {
            ChatMessage message = messages[i];
            projected.Add(message);
            if (message.Role != ChatRole.Tool) continue;

            pending.AddRange(PreviewsIn(message, viewCalls));

            // 一个回复的多个工具结果必须连在一起，图只能插在整串之后
            bool runEnds = i == messages.Count - 1 || messages[i + 1].Role != ChatRole.Tool;
            if (!runEnds || pending.Count == 0) continue;

            projected.Add(BuildImageMessage(pending, resolver, attachImages));
            pending.Clear();
        }

        return projected;
    }

    /// <summary>
    /// 一组消息投影出去会多带几张图、按多少 token 计。压缩在投影之前跑，看不见这些字节，靠它补上；
    /// 尺寸取工具结果里记下的预览尺寸，没记（老结果）按上限
    /// </summary>
    /// <param name="messages">消息（一个压缩组）</param>
    /// <returns>张数与 token 数</returns>
    public static (int Count, long Tokens) EstimateProjectedImages(IReadOnlyList<ChatMessage> messages)
    {
        HashSet<string> viewCalls = ViewCallsIn(messages);
        if (viewCalls.Count == 0) return (0, 0);

        List<ImagePreview.PreviewEntry> previews = messages
            .Where(m => m.Role == ChatRole.Tool)
            .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
            .Where(r => viewCalls.Contains(r.CallId))
            .SelectMany(r => ImagePreview.ParsePreviews(TextOf(r)))
            .ToList();
        long tokens = previews.Sum(p => (long)(p.Width > 0
            ? InlineImageLimits.EstimateTokens(p.Width, p.Height)
            : InlineImageLimits.MaxTokensPerImage));
        return (previews.Count, tokens);
    }

    private static HashSet<string> ViewCallsIn(IReadOnlyList<ChatMessage> messages) => messages
        .Where(m => m.Role == ChatRole.Assistant)
        .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
        .Where(ImagePreview.IsImageRead)
        .Select(c => c.CallId)
        .ToHashSet();

    private static IEnumerable<string> PreviewsIn(ChatMessage toolMessage, HashSet<string> viewCalls) => toolMessage
        .Contents.OfType<FunctionResultContent>()
        .Where(r => viewCalls.Contains(r.CallId))
        .SelectMany(r => ImagePreview.ParsePreviewPaths(TextOf(r)));

    private static ChatMessage BuildImageMessage(List<string> previews, AgentPathResolver resolver, bool attachImages)
    {
        StringBuilder text = new(attachImages ? "[Images attached by Read]" : "[Images read by Read]");
        List<AIContent> images = new();
        foreach (string preview in previews)
        {
            bool resolved = resolver.TryResolve(preview, out string full);
            // 看不了图也要占住这一条：那几轮助手回复说的是"我看到了图"，消息整条消失会让它无源可依。
            // 带上路径，模型还能把图转给 AnalyzeImage
            if (!attachImages)
            {
                text.Append("\n[Image omitted: this model cannot read images — ").Append(resolved ? full : preview)
                    .Append(']');
                continue;
            }

            if (resolved && File.Exists(full))
            {
                byte[] bytes = File.ReadAllBytes(full);
                images.Add(new DataContent(bytes, ImageFormats.Sniff(bytes) ?? ImageFormats.MediaTypeFromPath(full, "image/png")));
            }
            else
            {
                text.Append("\n[Image no longer available: ").Append(preview).Append(']');
            }
        }

        // 只放一个文本块：部分 OpenAI 兼容网关遇到多个文本块会 400
        return new ChatMessage(ChatRole.User, [new TextContent(text.ToString()), .. images]);
    }

    // 刚产出时是字符串，从历史文件读回来是 JsonElement
    private static string? TextOf(FunctionResultContent result) => result.Result switch
    {
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        null => null,
        var other => other.ToString(),
    };
}

/// <summary>
/// 挂在最内层（压缩之后）的投影客户端：工具组被折叠，结果没了，图也就跟着不发
/// </summary>
/// <param name="innerClient">下游客户端</param>
/// <param name="resolver">解析预览路径</param>
/// <param name="attachImages">是否真的附图；模型看不了图时传 false，只发占位</param>
internal sealed class ImagePreviewProjectingChatClient(IChatClient innerClient, AgentPathResolver resolver,
    bool attachImages = true)
    : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(ImagePreviewProjection.Project(messages.ToList(), resolver, attachImages), options,
            cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(ImagePreviewProjection.Project(messages.ToList(), resolver, attachImages), options,
            cancellationToken);
}
