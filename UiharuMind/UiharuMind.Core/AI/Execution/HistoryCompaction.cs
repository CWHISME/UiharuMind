/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Agents.AI.Compaction;

namespace UiharuMind.Core.AI.Execution;

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
            return budget > 0 && index.IncludedTokenCount > budget * fraction;
        };
    }
}
