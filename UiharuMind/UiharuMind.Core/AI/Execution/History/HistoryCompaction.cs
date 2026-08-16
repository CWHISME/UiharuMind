/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.History;

/// <summary>
/// 历史压缩策略的装配（见 ADR 0006）。两段式，与框架 <c>ContextWindowCompactionStrategy</c> 同构：
/// 先折叠老的工具结果（最温和，不动任何用户消息），到更高水位才截断最老的消息组。
///
/// <b>为什么不直接用框架那个现成的</b>：它的阈值在构造函数里就按上下文长度算死了，
/// 而本项目的 agent 只在切换工作区/权限档时重建，模型却可以随时切——用现成的那个，
/// 从 Deepseek(1M) 切到 GLM(128k) 之后预算仍停留在 1M，请求会必然超长。
/// 这里改用公开的两个原语加动态触发条件：<see cref="CompactionTrigger"/> 就是一个
/// <c>bool(CompactionMessageIndex)</c> 委托，阈值因此可以在每次触发时现读当前模型。
/// </summary>
public static class HistoryCompaction
{
    /// <summary>工具结果折叠的水位（占输入预算的比例）</summary>
    public const double ToolEvictionThreshold = 0.5;

    /// <summary>截断的水位（占输入预算的比例）。最后一道防线，必须高于交接文档的水位</summary>
    public const double TruncationThreshold = 0.9;

    private const int MinReserve = 512;
    private const int MaxReserve = 8192;

    /// <summary>
    /// 给回复与估算误差预留的 token。随上下文缩放而不是取固定值——固定 8192 会让一个
    /// 4096 上下文的本地模型算出负预算，构造阈值时直接抛。
    /// </summary>
    /// <param name="contextLength">模型上下文窗口</param>
    /// <returns>预留 token 数</returns>
    public static int ReserveFor(int contextLength)
    {
        return Math.Clamp(contextLength / 8, MinReserve, MaxReserve);
    }

    /// <summary>
    /// 可用于输入的 token 预算
    /// </summary>
    /// <param name="contextLength">模型上下文窗口；未知（&lt;=0）时返回 0 表示不压缩</param>
    /// <returns>输入预算</returns>
    public static int InputBudgetFor(int contextLength)
    {
        if (contextLength <= 0) return 0;
        return Math.Max(1, contextLength - ReserveFor(contextLength));
    }

    /// <summary>
    /// 装配压缩策略
    /// </summary>
    /// <param name="contextSource">当前模型上下文窗口的来源，每次触发时现读</param>
    /// <returns>压缩策略</returns>
    internal static CompactionStrategy Create(Func<int> contextSource)
    {
        // 停止条件留空:框架默认取触发条件的反面,正是我们要的"压到不再触发为止"
        return new PipelineCompactionStrategy(
        [
            new ToolResultCompactionStrategy(ExceedsFraction(contextSource, ToolEvictionThreshold)),
            new TruncationCompactionStrategy(ExceedsFraction(contextSource, TruncationThreshold)),
        ]);
    }

    // 预算未知(没有模型在跑)时一律不压缩:此时请求本来就发不出去,压缩只会白白毁掉历史
    private static CompactionTrigger ExceedsFraction(Func<int> contextSource, double fraction)
    {
        return index =>
        {
            int budget = InputBudgetFor(contextSource());
            return budget > 0 && CorrectedTokenCount(index) > budget * fraction;
        };
    }

    // [MFA绕坑] 绕:自己重算图片的 token 数 因:框架把非文本内容一律按 字节数/4 估,且没有注入 Tokenizer 的口子 删除条件:CompactionProvider 允许传 Tokenizer 或框架按模态计价
    /// <summary>
    /// 修正框架估算后的已计入 token 数。
    ///
    /// 框架把图片按 <c>字节数 / 4</c> 估：一张 150KB 的截图会被算成 3.7 万 token，
    /// 而它真实只值一两千——虚高二三十倍。后果不是多花钱，是<b>压缩被凭空提前触发</b>，
    /// 三张图就能把 128k 模型顶过截断水位去砍真实对话。所以这里把图片那部分换成
    /// <see cref="InlineImageLimits.MaxTokensPerImage"/>。
    ///
    /// <b>必须按组抵消而不是按条</b>：无 <c>Tokenizer</c> 时框架是「整组字节 ÷ 4」除一次
    /// （<c>CompactionMessageIndex.CreateGroup</c>），逐条相减会带进舍入偏差。
    ///
    /// <b>单调性是承重的</b>：截断策略靠「排除一组 → 重问一次条件」收敛，条件必须随排除单调下降。
    /// 每组修正后的贡献 =（非图片字节 ÷ 4）+ 图片数 × 上界 ≥ 0，因此排除任意一组都只会让总数变小。
    /// </summary>
    /// <param name="index">框架给出的消息分组索引</param>
    /// <returns>修正后的 token 数</returns>
    internal static long CorrectedTokenCount(CompactionMessageIndex index)
    {
        long total = 0;
        foreach (CompactionMessageGroup group in index.Groups)
        {
            if (group.IsExcluded) continue;
            total += CorrectedGroupTokens(group.ByteCount, group.TokenCount, group.Messages);
        }

        return total;
    }

    /// <summary>
    /// 单个消息组修正后的 token 数。
    /// 单独拆出来是为了可测：框架的 <c>CompactionMessageGroup</c> 构造函数是 internal，
    /// 测试项目造不出 <see cref="CompactionMessageIndex"/>，而判断逻辑全在这一层。
    /// </summary>
    /// <param name="groupByteCount">框架算出的该组字节数</param>
    /// <param name="groupTokenCount">框架算出的该组 token 数</param>
    /// <param name="messages">该组的消息</param>
    /// <returns>修正后的 token 数；不含图片时原样返回 <paramref name="groupTokenCount"/></returns>
    internal static long CorrectedGroupTokens(int groupByteCount, int groupTokenCount,
        IReadOnlyList<ChatMessage> messages)
    {
        (int imageBytes, int imageCount) = ImagePayloadOf(messages);
        // 不含图片的组原样采用框架的数:那一侧的估算本来就够准,也不必假设它是怎么算出来的
        if (imageCount == 0) return groupTokenCount;

        return (groupByteCount - imageBytes) / 4 + (long)imageCount * InlineImageLimits.MaxTokensPerImage;
    }

    /// <summary>
    /// 统计一组消息里图片内容的字节数与张数
    /// </summary>
    /// <param name="messages">消息</param>
    /// <returns>图片总字节数与张数；字节口径与框架的 <c>ComputeContentByteCount</c> 一致</returns>
    private static (int Bytes, int Count) ImagePayloadOf(IReadOnlyList<ChatMessage> messages)
    {
        int bytes = 0;
        int count = 0;
        foreach (ChatMessage message in messages)
        {
            foreach (AIContent content in message.Contents)
            {
                if (content is not DataContent data || !data.HasTopLevelMediaType("image")) continue;

                // 与框架同口径:数据体 + MediaType + Name 的 UTF-8 字节数
                bytes += data.Data.Length + ByteCountOf(data.MediaType) + ByteCountOf(data.Name);
                count++;
            }
        }

        return (bytes, count);
    }

    private static int ByteCountOf(string? value)
    {
        return string.IsNullOrEmpty(value) ? 0 : Encoding.UTF8.GetByteCount(value);
    }
}
