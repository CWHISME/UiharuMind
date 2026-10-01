using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 把 <see cref="ViewImageTool"/> 结果里的预览图投影成一条 user 消息，插在那一串 tool 消息之后（ADR 0053）。
/// 只改发出去的那份，历史不动——于是渲染、裁剪、编辑重试、删除闭包都不必认识一种新消息。
/// </summary>
public static class ViewImageProjection
{
    /// <summary>
    /// 投影一次请求的消息
    /// </summary>
    /// <param name="messages">将要发出的消息（历史 + 本轮）</param>
    /// <param name="resolver">解析预览路径（草稿目录简写）</param>
    /// <returns>投影后的消息；没有 ViewImage 结果时原样返回同一实例</returns>
    public static IReadOnlyList<ChatMessage> Project(IReadOnlyList<ChatMessage> messages, AgentPathResolver resolver)
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

            projected.Add(BuildImageMessage(pending, resolver));
            pending.Clear();
        }

        return projected;
    }

    /// <summary>
    /// 一组消息投影出去会多带几张图。压缩在投影之前跑，看不见这些字节，靠它补上
    /// </summary>
    /// <param name="messages">消息（一个压缩组）</param>
    /// <returns>张数</returns>
    public static int CountProjectedImages(IReadOnlyList<ChatMessage> messages)
    {
        HashSet<string> viewCalls = ViewCallsIn(messages);
        return viewCalls.Count == 0
            ? 0
            : messages.Where(m => m.Role == ChatRole.Tool).Sum(m => PreviewsIn(m, viewCalls).Count());
    }

    private static HashSet<string> ViewCallsIn(IReadOnlyList<ChatMessage> messages) => messages
        .Where(m => m.Role == ChatRole.Assistant)
        .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
        .Where(c => c.Name == ViewImageTool.ToolName)
        .Select(c => c.CallId)
        .ToHashSet();

    private static IEnumerable<string> PreviewsIn(ChatMessage toolMessage, HashSet<string> viewCalls) => toolMessage
        .Contents.OfType<FunctionResultContent>()
        .Where(r => viewCalls.Contains(r.CallId))
        .SelectMany(r => ViewImageTool.ParsePreviewPaths(TextOf(r)));

    private static ChatMessage BuildImageMessage(List<string> previews, AgentPathResolver resolver)
    {
        StringBuilder text = new("[Images attached by ViewImage]");
        List<AIContent> images = new();
        foreach (string preview in previews)
        {
            if (resolver.TryResolve(preview, out string full) && File.Exists(full))
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
internal sealed class ViewImageProjectingChatClient(IChatClient innerClient, AgentPathResolver resolver)
    : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(ViewImageProjection.Project(messages.ToList(), resolver), options, cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(ViewImageProjection.Project(messages.ToList(), resolver), options,
            cancellationToken);
}
