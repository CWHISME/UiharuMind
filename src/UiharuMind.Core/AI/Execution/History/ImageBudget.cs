/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.History;

/// <summary>
/// 发请求前把<b>最老的几张图</b>换回一句占位文本，让整个请求的图片字节落回预算之内。
///
/// <para>
/// 为什么需要它：图片进历史之后每一轮都重传，而预览图（<c>ImagePreviewProjection</c>）与内联附件
/// 都是<b>只进不出</b>。实测两个子会话涨到 12.3MB / 14.3MB 后，上游每次都只回一帧
/// <c>Error processing stream start</c>，此后每一发都是同一个 400——请求不会再变小，怎么重试都一样。
/// </para>
///
/// <para>
/// <b>代价说清楚</b>：裁掉的那条图在历史靠前的位置，改它等于从那里往后整段前缀缓存作废。
/// 所以这里的算法不是「超了就裁」，而是带<b>回差</b>：一次裁到 target 之下，之后要再攒够 trigger − target 才动下一刀。
/// 按张数开窗（<c>HistoryImageWindow</c> 那种）是每来一张新图断一次、六十张断六十次；
/// 回差把同一件事压下一个数量级，但它不消除代价——阈值越紧断得越勤，调阈值前先把这一条算进去
/// （默认 32/16MiB 下 60 张 400KB 的图一次都不裁）。
/// </para>
///
/// <para>
/// 只改发出去的那一份：会话文件、界面、草稿目录里的预览全都原样，下次要看得清还能重新读。
/// 与 <see cref="InlineImageDegrading"/>（模型看不了图）和 <see cref="ImagePreviewProjection"/>
/// （把预览投影成消息）同一条口径。
/// </para>
///
/// <para>
/// 它挂在改写链的最里层（投影之内、传输之前），因为只有那里才看得见最终那份字节：
/// 压缩看不见投影的图，投影看不见自己拼完之后的总量。
/// </para>
/// </summary>
public static class ImageBudget
{
    /// <summary>
    /// 按预算裁一份要发出去的消息
    /// </summary>
    /// <param name="messages">将要发出的消息（历史 + 本轮 + 投影）</param>
    /// <param name="limits">预算</param>
    /// <returns>裁好的消息；没越线时原样返回同一实例</returns>
    public static IReadOnlyList<ChatMessage> Apply(IReadOnlyList<ChatMessage> messages, ImageBudgetLimits limits)
    {
        if (limits.IsOff) return messages;

        List<long> sizes = ImageSizes(messages);
        if (sizes.Count == 0) return messages;

        (int cut, long droppedBytes) = CutFor(sizes, limits);
        if (cut <= 0) return messages;

        long total = 0;
        foreach (long size in sizes) total += size;

        Log.Debug($"[image budget] dropped {cut}/{sizes.Count} oldest images " +
                  $"({total / 1024.0 / 1024:0.0}MB → {(total - droppedBytes) / 1024.0 / 1024:0.0}MB of wire bytes, " +
                  $"trigger {limits.TriggerBytes / 1024.0 / 1024:0.#}MB / {limits.MaxCount} images).");
        return Replace(messages, cut);
    }

    /// <summary>
    /// 该丢几张。两条线（字节 / 张数）各自带回差：越过任何一条就一次丢到两条都满足，
    /// 之后要再攒够 trigger − target 才动下一刀。
    ///
    /// <para>
    /// <b>必须先算总量再裁，不能边走边裁</b>：边走边裁时，中途腾出来的余量会被后面的尾巴重新吃掉，
    /// 最后一刀往往把总量停在触发线上（10 张各 1MiB、4/2 时会留下 4 张 = 正好 4MiB），
    /// 「下一次裁剪要等新图攒够差额」当场不成立——一张新图进来就得再断一次前缀。
    /// </para>
    ///
    /// <b>至少留一张</b>：单张就超过停止线时（未注入缩图器的 CLI、或 target 配得比一张图还小），
    /// 宁可变大超标也不能一张不剩——模型看到「这一轮没有图」比请求大一圈更难查
    /// </summary>
    /// <param name="sizes">按出现顺序排好的图片字节数（已折成传输字节）</param>
    /// <param name="limits">预算</param>
    /// <returns>丢最老的前几张，以及它们合起来多少字节</returns>
    private static (int Cut, long DroppedBytes) CutFor(IReadOnlyList<long> sizes, ImageBudgetLimits limits)
    {
        long total = 0;
        foreach (long size in sizes) total += size;

        if (!OverTrigger(total, sizes.Count, limits)) return (0, 0);

        int cut = 0;
        long dropped = 0;
        // cut < sizes.Count - 1 是「至少留一张」：最后一张是最新的，不许连它一起丢
        while (cut < sizes.Count - 1 && OverTarget(total - dropped, sizes.Count - cut, limits))
        {
            dropped += sizes[cut];
            cut++;
        }

        return (cut, dropped);
    }

    /// <summary>越线了：两条线任意一条越过触发值</summary>
    private static bool OverTrigger(long keptBytes, int keptCount, ImageBudgetLimits limits) =>
        (limits.UsesBytes && keptBytes > limits.TriggerBytes)
        || (limits.UsesCount && keptCount > limits.MaxCount);

    /// <summary>还没裁够：仍有任意一条在停止线之上</summary>
    private static bool OverTarget(long keptBytes, int keptCount, ImageBudgetLimits limits) =>
        (limits.UsesBytes && keptBytes > limits.EffectiveTarget)
        || (limits.UsesCount && keptCount > limits.EffectiveCountFloor);

    /// <summary>
    /// 按出现顺序收集所有内联图片的<b>传输字节</b>（base64 长度，不是解码后的长度）。
    ///
    /// 用它而不是 <c>Data.Length</c>：撞网关的是请求体，图片在里面是 base64，比原始字节多三分之一。
    /// 按原始字节算，配置里的 8MiB 实际对应 10.7MB 的请求体，余量远没有看上去那么多
    /// </summary>
    /// <param name="messages">要发出去的消息</param>
    /// <returns>各图传输字节数，按出现顺序</returns>
    private static List<long> ImageSizes(IReadOnlyList<ChatMessage> messages)
    {
        List<long> sizes = [];
        foreach (ChatMessage message in messages)
        {
            foreach (AIContent content in message.Contents)
            {
                if (!ImageContentRewrite.IsImage(content)) continue;
                int length = ((DataContent)content).Data.Length;
                sizes.Add((long)(length + 2) / 3 * 4); //base64:每 3 字节 4 个字符
            }
        }

        return sizes;
    }

    /// <summary>
    /// 把最老的前 <paramref name="cut"/> 张图换成占位文本。没有图被裁的消息保持同一实例
    /// </summary>
    /// <param name="messages">原消息</param>
    /// <param name="cut">要裁掉几张（从最老的算起）</param>
    /// <returns>新消息列表</returns>
    private static IReadOnlyList<ChatMessage> Replace(IReadOnlyList<ChatMessage> messages, int cut)
    {
        List<ChatMessage> result = new(messages.Count);
        int seen = 0;
        foreach (ChatMessage message in messages)
        {
            int total = message.Contents.Count(ImageContentRewrite.IsImage);
            int from = seen;
            seen += total;

            int dropped = Math.Clamp(cut - from, 0, total);
            result.Add(dropped == 0
                ? message
                : ImageContentRewrite.DropImages(message, dropped, ImagePlaceholder.BudgetTrimmed));
        }

        return result;
    }
}

/// <summary>
/// 挂在最里层的预算层：投影拼完之后、真正发出去之前数一遍字节
/// </summary>
/// <param name="innerClient">下游客户端</param>
/// <param name="limits">预算；每次请求现读（配置改了不必重启）</param>
internal sealed class ImageBudgetingChatClient(IChatClient innerClient, Func<ImageBudgetLimits> limits)
    : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(ImageBudget.Apply(messages.ToList(), limits()), options, cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(ImageBudget.Apply(messages.ToList(), limits()), options, cancellationToken);
}
