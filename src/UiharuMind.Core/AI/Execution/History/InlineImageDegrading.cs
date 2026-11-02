using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.AI.Execution.History;

/// <summary>
/// 模型自己看不了图时，把<b>发出去的那一份</b>里的内联图片换成一句占位文本。
///
/// 用户附件在视觉模型下是直接把字节内联进消息的（<c>AttachmentTrayViewData.BuildUserMessage</c>），
/// 字节于是随消息落进历史，此后每轮重发。而「决定要不要内联」只发生在<b>组消息那一刻</b>，
/// 中途换成不支持图片的模型，历史里那些字节没有任何一层会把它们摘掉：严格的服务端直接 400
/// （实测 CommandCode 的 <c>reflection/beam:free</c>：「This conversation contains an image」，
/// 且不可重试——历史不改就永远发不出去），宽松的服务端静默忽略（实测商汤那边收下图、什么都不说），
/// 所以这个问题在换到严格服务商之前一直没显形。
///
/// 只改发出去的那份，历史一个字不动——与 <see cref="ImagePreviewProjection"/> 同一口径：
/// 气泡里照样看得见图，会话文件里那张图也还在，换回视觉模型就是原样。
///
/// <b>占位不带路径</b>（与投影不同，投影那份要带是为了让模型能把图转给 <c>AnalyzeImage</c>）：
/// agent 形态内联时路径已写进正文（<c>[Attached file: …]</c>），非 agent 形态压根没有工具，
/// 抄一遍只会多花 token。
///
/// 与已停用的 <c>HistoryImageWindow</c> 的区别值得记住：那个是「只留最近 N 条」的窗口，
/// 代价是每来一张新图就改写一条靠前的历史、从那里往后的服务端前缀缓存全部失效；
/// 这一层没有这个代价——模型手上本来就没有可命中的前缀，它压根没收到过那些字节。
/// </summary>
public static class InlineImageDegrading
{
    /// <summary>
    /// 把一份要发出去的消息里的内联图片换成占位文本
    /// </summary>
    /// <param name="messages">将要发出的消息（历史 + 本轮）</param>
    /// <returns>换好的消息；一条带图的都没有时原样返回同一实例</returns>
    public static IReadOnlyList<ChatMessage> Project(IReadOnlyList<ChatMessage> messages)
    {
        List<ChatMessage>? projected = null;
        for (int i = 0; i < messages.Count; i++)
        {
            ChatMessage message = messages[i];
            ChatMessage degraded = Degrade(message);
            if (ReferenceEquals(degraded, message))
            {
                projected?.Add(message);
                continue;
            }

            // 第一次撞见带图的消息才回头复制前面的：没有图的会话一个对象都不多造
            if (projected == null)
            {
                projected = new List<ChatMessage>(messages.Count);
                for (int j = 0; j < i; j++) projected.Add(messages[j]);
            }

            projected.Add(degraded);
        }

        return projected ?? messages;
    }

    /// <summary>
    /// 一段消息里有多少张内联图片。界面侧据此在切模型前说清代价（见会话模型面板）
    /// </summary>
    /// <param name="messages">消息；null 当作一条都没有</param>
    /// <returns>图片张数</returns>
    public static int CountIn(IEnumerable<ChatMessage>? messages) =>
        messages?.Sum(message => message.Contents.Count(IsImage)) ?? 0;

    /// <summary>
    /// 复制一条消息，把图片换成占位文本，其余内容原样保留。<b>占位并进原来那块文字里</b>：
    /// 部分 OpenAI 兼容网关遇到多个文本块会 400（同 <see cref="ImagePreviewProjection"/>）
    /// </summary>
    /// <param name="message">原消息</param>
    /// <returns>新消息；没有图片时返回同一个实例</returns>
    private static ChatMessage Degrade(ChatMessage message)
    {
        int images = 0;
        int textIndex = -1;
        for (int i = 0; i < message.Contents.Count; i++)
        {
            if (IsImage(message.Contents[i])) images++;
            else if (textIndex < 0 && message.Contents[i] is TextContent) textIndex = i;
        }

        if (images == 0) return message;

        string note = ImagePlaceholder.Omitted(images, null);
        List<AIContent> kept = new(message.Contents.Count);
        for (int i = 0; i < message.Contents.Count; i++)
        {
            AIContent content = message.Contents[i];
            if (IsImage(content)) continue;
            kept.Add(i == textIndex ? AppendNote((TextContent)content, note) : content);
        }

        // 整条只有图（用户只贴了张截图、一个字没打）：占位自己成一块文本
        if (textIndex < 0) kept.Add(new TextContent(note));

        return ChatMessageCopies.WithContents(message, kept);
    }

    private static TextContent AppendNote(TextContent text, string note) =>
        new(string.IsNullOrEmpty(text.Text) ? note : $"{text.Text}\n\n{note}")
        {
            AdditionalProperties = text.AdditionalProperties,
        };

    private static bool IsImage(AIContent content) =>
        content is DataContent data && data.HasTopLevelMediaType("image");
}

/// <summary>
/// 挂在改写链上的降级客户端。<b>判据每次请求现读</b>（<paramref name="modelCannotSee"/>）：
/// 固化在装配期就会两头错——普通对话形态的装配快照不含视觉一项、换模型也不重建，
/// 于是「视觉→盲」该降的不降、「盲→视觉」反而把刚发的图也抹掉（见 <see cref="ServingVision"/>）。
/// </summary>
/// <param name="innerClient">下游客户端</param>
/// <param name="modelCannotSee">此刻这一发的模型看不看得见图（false 时原样放行）</param>
internal sealed class InlineImageDegradingChatClient(IChatClient innerClient, Func<bool> modelCannotSee)
    : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(Project(messages), options, cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(Project(messages), options, cancellationToken);

    private IReadOnlyList<ChatMessage> Project(IEnumerable<ChatMessage> messages)
    {
        List<ChatMessage> sent = messages.ToList();
        return modelCannotSee() ? InlineImageDegrading.Project(sent) : sent;
    }
}
