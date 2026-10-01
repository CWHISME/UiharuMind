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
using UiharuMind.Core.AI.Execution.Assembly;

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
    /// <summary>
    /// 工具结果折叠的水位（占历史额度的比例）。<b>必须高于交接文档的比例</b>：折叠每次请求都判，交接只在轮末写，
    /// 折叠线一低，历史每次都先撞上它——先折一次断一次缓存，轮末交接再断一次。
    /// 只要高于交接的比例，任何固定开销下折叠线都在交接线之上：
    /// 额度 × 0.85 −（预算 × 0.8 − 固定开销）= 预算 × 0.05 + 固定开销 × 0.15 &gt; 0。
    ///
    /// 于是折叠只剩两种情形：单轮内一路冲过交接线（轮末照常交接），以及子代理那种一轮到底、轮间根本没有交接的长任务
    /// </summary>
    public const double ToolEvictionThreshold = 0.85;

    /// <summary>
    /// 折叠一次腾到这里（回差）。只折到刚好不触发的话，之后每多一条工具结果就再折最老的一组，
    /// 几乎每次调用都整段不中缓存；一次折出一截余量（额度的 20%），断一次缓存换后面多次命中。
    /// 实际占用因此在它与 <see cref="ToolEvictionThreshold"/> 之间来回（折法见 <c>InSteps</c>）
    /// </summary>
    public const double ToolEvictionTarget = 0.65;

    /// <summary>截断的水位（占历史额度的比例）。最后一道防线，必须高于交接文档与折叠的水位</summary>
    public const double TruncationThreshold = 0.9;

    /// <summary>
    /// 截断一次删到这里（回差），一级台阶同样是额度的 20%。不留余量的话，越线之后每多一条消息就再删最老的一组，
    /// 岔口紧跟在系统提示之后，这一轮剩下的每次请求几乎整段不中缓存（本地 llama.cpp 就是整段重算）。
    /// 代价是一次多丢一截最老的消息——反正马上要丢，早丢一截换后面多次命中
    /// </summary>
    public const double TruncationTarget = 0.7;

    private const int MinReserve = 512;
    private const int MaxReserve = 8192;

    // 折叠与截断都不碰的最近几组，取框架 ContextWindowCompactionStrategy 的值。两个类单用时的默认是 16 与 32，
    // 按组数而不按 token：一次并行读十几个文件只算一组，最近几组就装下了几乎全部历史，折与截都无从下手，请求直接超窗
    private const int PreservedGroups = 2;

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
    /// 历史额度：输入预算减去每轮固定开销，即<b>历史消息真正能占的那部分</b>。
    /// 折叠与截断的水位乘在它上面，不是乘在输入预算上。
    ///
    /// 从前乘在输入预算上是错的，而且错得看着很合理：分子只数消息——系统提示与工具定义
    /// 都不是消息，进不了 <c>CompactionMessageIndex</c>——分母却是全量预算。于是一套 21k 的
    /// MCP 工具集能让截断在请求<b>早已超出上下文之后</b>才动手：128k 模型配 25.6k 固定开销时，
    /// 旧算法的截断水位是 107827，加上固定开销已是 133427，超了 5.4k。
    /// 这与服务端报不报得准无关，任何模型配任何大工具集都中。见 ADR 0009。
    /// </summary>
    /// <param name="contextLength">模型上下文窗口</param>
    /// <param name="fixedOverhead">每轮固定开销（系统提示 + 工具定义）</param>
    /// <returns>历史额度；固定开销已吃光预算时为 0</returns>
    public static int HistoryQuotaFor(int contextLength, int fixedOverhead)
    {
        return Math.Max(0, InputBudgetFor(contextLength) - Math.Max(0, fixedOverhead));
    }

    /// <summary>
    /// 装配压缩策略
    /// </summary>
    /// <param name="contextSource">当前模型上下文窗口的来源，每次触发时现读</param>
    /// <param name="estimate">本轮输入估算；这里读它的固定开销，并回写历史估算</param>
    /// <returns>压缩策略</returns>
    internal static CompactionStrategy Create(Func<int> contextSource, TurnInputEstimate estimate)
    {
        // 折叠是第一道，此刻还一组没排除：它看到的就是原始历史，交接水位要读这个数（见 TurnUsageLedger.RawInput）
        (CompactionTrigger Trigger, CompactionTrigger Target) folding = InSteps(contextSource, estimate,
            ToolEvictionThreshold, ToolEvictionTarget, raw => estimate.LastRawHistory = raw);
        (CompactionTrigger Trigger, CompactionTrigger Target) truncation = InSteps(contextSource, estimate,
            TruncationThreshold, TruncationTarget);
        return new PipelineCompactionStrategy(
        [
            new ToolResultCompactionStrategy(folding.Trigger, PreservedGroups, folding.Target)
            {
                // 默认格式把结果原文照抄，折了等于没折（见 ToolCallFolding）
                ToolCallFormatter = ToolCallFolding.Format,
            },
            new TruncationCompactionStrategy(truncation.Trigger, PreservedGroups, truncation.Target),
        ]);
    }

    // [MFA绕坑] 绕:只看原始大小定台阶的无状态回差 因:压缩挂在最内层 reducer 上,每次从原始历史重建索引(见 AgentAssembler.MoveCompactionToLeaf),折过哪些无处可记;框架原路 CompactionProvider 把索引存进会话状态、增量追加,固定停止线就自带回差 删除条件:CompactionProvider 认得本地哨兵、改回框架原路,停止线直接写成「低于额度 × 停止比例」
    /// <summary>
    /// 按台阶压缩的触发与停止条件，折叠与截断共用。
    ///
    /// 每次调用拿到的都是<b>原始</b>历史（存下的不压，见 <c>AgentAssembler.MoveCompactionToLeaf</c>），
    /// 「上次压过哪些」无处可记，普通的回差因此失效：大小一直在触发线上方，每次都按停止线重压，多一条就多压一组。
    /// 改成只看这一道开始时的大小 S：它每越过一级台阶（额度 × (触发 − 停止)），就再腾出一级。同一级里要腾的量不变，
    /// 历史只往后长、压的总是最老那几组，于是压哪些组也不变，前缀逐字稳定；跨级才多压一截、断一次缓存。
    /// 实际占用落在停止线与触发线之间，效果与回差相同，而且不依赖任何状态——重建、重启都一样。
    /// 截断那一道的 S 是折叠之后的大小：折叠同样按台阶走，同一级里它也是稳定的。
    ///
    /// 两个条件在同一次压缩里先后调用（先问触发、再逐组问停止），所以用闭包带着这一次的 S；
    /// 同一个 agent 的服务调用是一次接一次的，不会交错。每次问都顺手回写历史估算，
    /// 停止条件逐组重问，最后落下的是压完之后的值——那正是本轮真会发出去的历史大小。
    ///
    /// 额度为 0 时一律不压缩——两种成因：预算未知（没有模型在跑），或固定开销自己就吃光了预算。
    /// 两种情况下请求本来就发不出去，压缩只会白白毁掉历史。
    ///
    /// 比水位时固定开销与历史都乘上校准系数（见 <see cref="TurnInputEstimate.Calibrate"/>），回写的估算不乘
    /// </summary>
    private static (CompactionTrigger Trigger, CompactionTrigger Target) InSteps(Func<int> contextSource,
        TurnInputEstimate estimate, double threshold, double target, Action<long>? onStart = null)
    {
        long start = 0; //这一道开始时的历史大小（我们的口径）
        double scale = 1; //这一次压缩用的校准系数：触发与停止必须同一个
        CompactionTrigger trigger = index =>
        {
            start = CorrectedTokenCount(index);
            onStart?.Invoke(start);
            estimate.LastHistory = start;
            scale = estimate.Calibration;
            int quota = CalibratedQuota(contextSource(), estimate, scale);
            return quota > 0 && start * scale > quota * threshold;
        };
        CompactionTrigger stop = index =>
        {
            long now = CorrectedTokenCount(index);
            estimate.LastHistory = now;
            int quota = CalibratedQuota(contextSource(), estimate, scale);
            double step = quota * (threshold - target);
            if (step <= 0) return true;
            double steps = Math.Floor((start * scale - quota * threshold) / step) + 1;
            return (start - now) * scale >= steps * step;
        };
        return (trigger, stop);
    }

    private static int CalibratedQuota(int contextLength, TurnInputEstimate estimate, double scale)
    {
        return HistoryQuotaFor(contextLength, (int)Math.Ceiling(estimate.FixedOverhead * scale));
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
        // 发送时才投影进去的图(ADR 0053):字节不在这组里,只按张数补上界
        imageCount += ViewImageProjection.CountProjectedImages(messages);
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
