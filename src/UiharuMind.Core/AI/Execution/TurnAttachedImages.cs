using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 「这张图已经在用户这一轮的消息里、模型此刻就看得见」的判据。Read 读到这种图时只回一句提示：
/// 不再烘一份预览副本、不再多投影一条带图消息——一次白付一张图的 token
/// （上限 <see cref="InlineImageLimits.MaxTokensPerImage"/>，且此后每轮重发）。
/// 首次那次 Read 的往返省不掉（调用已经发出去了），省的是回读循环与那份重发的图。
///
/// 起因是 agent 形态组装用户消息时，内联的图片旁边<b>还写着一条</b> <c>[Attached file: 路径]</c>
/// （路径要留给 Edit 一类工具，见 <c>AttachmentTrayViewData.BuildUserMessage</c>），
/// 而 Read 自称能看图，模型于是常沿着这个路径再读一遍。
///
/// <b>只看供给区间里的最后一条用户消息</b>，不看「历史上附加过」：老消息会被压缩折叠、被交接文档截断
/// （<c>HistorySupply.From</c>），那时图已经不在请求里，按历史拒绝等于让模型永远看不到它——
/// 一次多余的读是浪费，这个是信息缺失。所以扫描范围就取同一份供给区间（不是历史快照）：
/// 无输入轮（助手锚点重试、唤醒轮）里最后一条用户消息可能落在交接文档之前，那一条图已经不在请求里。
/// 另两条必要条件：这一发的模型真的看得见图
/// （否则发送时会降级成占位，见 <c>InlineImageDegrading</c>，
/// 判据与它同源，见 <see cref="ServingModelFacts"/>），以及字节逐一致
/// （组装内联与 Read 预览用的是同一个缩图器，见 App 启动时的注入）。
///
/// 只在<b>本轮第一次工具调用</b>里拦：折叠与截断都只保留最近两组（<c>HistoryCompaction.PreservedGroups</c>），
/// 而分组规则是「用户消息自成一组、带工具调用的助手消息连同它的结果算一组」——实测
/// <b>第二次服务调用时本轮开头那条消息仍是倒数第二组、受保护</b>，第三次起才可能被删
/// （每轮 3 万字工具结果时第 3 发开始消失）。判据取更严的一侧：多付一次读是浪费，骗模型看图是损失。
///
/// 已知边界：上下文注入（世界设定、知识库）的 User 消息每条自成一组，正好占满保留窗，
/// 所以本轮提问本来就在保护圈外——只是截断从最老一头删、删够就停。注入合计逼近停止线时
/// 它会被整条删掉，而本判据仍会命中（详见 ADR 0073 第三条隐性前提）。代价有界：
/// 接下来那次 Read 已经过了第一次工具调用、照常附图。
///
/// 为什么工具跑的时候用户消息已经在历史里：落盘是<b>每次服务调用</b>一次
/// （<c>ChatSession.TurnStartedAt</c> 那份一次性凭据就是按这个消费的），本轮第一次调用的请求
/// 里就有这条用户消息，它先落盘、工具随后才执行。万一哪天这个次序变了，也只是判据不成立、
/// 退回从前的重复读，不会错拦。
/// </summary>
internal sealed class TurnAttachedImages(Func<ChatSession?>? session, Func<bool> seesImagesNow)
{
    /// <summary>
    /// 这些字节（要发给模型的那一份）是不是已经躺在这一轮的用户消息里
    /// </summary>
    /// <param name="image">已经缩好的图片字节</param>
    /// <returns>已经附在眼前则为 true</returns>
    public bool Contains(byte[] image)
    {
        if (image.Length == 0 || !seesImagesNow()) return false;
        if (session?.Invoke() is not { } chat) return false;

        // 跨线程读：这一轮可能正往同一份历史里追加，见 SessionHistory.Snapshot。
        // 范围与供给同源（从最后一份交接文档起）：交接之前的消息不会发给模型
        IReadOnlyList<ChatMessage> supplied = HistorySupply.From(chat.History.Snapshot(), promptOnly: false);
        for (int i = supplied.Count - 1; i >= 0; i--)
        {
            // 倒着找第一条用户消息就是本轮的输入；再往前的属于已经翻过去的轮次
            if (supplied[i].Role != ChatRole.User) continue;
            // 只在本轮第一次工具调用里拦：折叠与截断只保留最近两组，而用户消息自成一组、
            // 带工具调用的助手消息连同结果算另一组——实测第二次调用时它还是倒数第二组、受保护，
            // 第三次起才可能被删，图那时可能不在请求里，拦了就是让模型看不到它。多付一次读比这个便宜
            return !HasToolResultAfter(supplied, i) && Carries(supplied[i], image);
        }

        return false;
    }

    private static bool HasToolResultAfter(IReadOnlyList<ChatMessage> supplied, int index)
    {
        for (int i = index + 1; i < supplied.Count; i++)
        {
            if (supplied[i].Role == ChatRole.Tool) return true;
        }

        return false;
    }

    private static bool Carries(ChatMessage message, byte[] image) => message.Contents
        .OfType<DataContent>()
        .Any(content => content.HasTopLevelMediaType("image") && content.Data.Span.SequenceEqual(image));
}
