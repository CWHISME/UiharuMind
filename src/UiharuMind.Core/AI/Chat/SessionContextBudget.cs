/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.AI.Chat;

/// <summary>
/// 会话的上下文预算：模型窗口与会话自设上限合出来的那一个数。
///
/// <b>唯一入口</b>。压缩水位、交接判据、界面上的百分比与分母，全都得从这里取值——
/// 各读各的就会出现「环显示 40%、交接却按 128k 判」，而那正是这套显示当初要消灭的误解。
///
/// 自设上限只收紧不放宽（见 <see cref="Resolve"/>），所以下限这件事是真实存在的：
/// 压到当前占用之下，下一发就会立刻越过交接线，用户只想「收紧一点」，得到的是当场写一份记忆压缩。
/// 允许区间的算法见 <see cref="MinimumFor"/>。
/// </summary>
public static class SessionContextBudget
{
    /// <summary>允许区间按 1k 取整：这个值要给人看、给人调，128347 这种数没有意义</summary>
    private const int Rounding = 1000;

    /// <summary>
    /// 合出本次生效的上下文上限
    /// </summary>
    /// <param name="modelContextLength">模型真实窗口；0 表示未知</param>
    /// <param name="overrideLength">会话自设上限；0 表示不设</param>
    /// <returns>生效上限；两者都没有时为 0</returns>
    public static int Resolve(int modelContextLength, int overrideLength)
    {
        if (overrideLength <= 0) return Math.Max(0, modelContextLength);
        // 模型未知时不夹：宁可让服务端当场报错（响亮、可去模型编辑里填数），
        // 也不要静默按一个更小的窗口把长对话截掉
        if (modelContextLength <= 0) return overrideLength;
        return Math.Min(overrideLength, modelContextLength);
    }

    /// <summary>
    /// 允许设置的最小上限。低于它，下一次请求就会越过交接线（<see cref="HistoryHandoff.Threshold"/> 占输入预算），
    /// 等于「一设完就当场写一份记忆压缩」——那是一次完整请求、还会把前缀缓存整段打断。
    ///
    /// 由 <c>usage &lt; threshold × (cap − reserve)</c> 反解，再向上取整到 1k。
    /// 当前没有占用时返回 0：没有「低于占用」这回事，想设多小都行。
    /// </summary>
    /// <param name="usage">当前占用（有效占用，见 <see cref="TurnUsageLedger.EffectiveInput"/>）</param>
    /// <param name="modelContextLength">模型真实窗口，用来算预留；0 表示未知</param>
    /// <returns>最小可设上限；没有占用时为 0</returns>
    public static int MinimumFor(long usage, int modelContextLength)
    {
        if (usage <= 0) return 0;
        // 预留按模型窗口算而不是按候选上限算:候选值正是要解的那个未知量,
        // 而预留随窗口增长得很慢(窗口的 1/8,夹在 512~8192),用模型窗口代进去足够准
        int reserve = HistoryCompaction.ReserveFor(Math.Max(modelContextLength, (int)Math.Min(usage, int.MaxValue)));
        long need = (long)Math.Ceiling(usage / HistoryHandoff.Threshold) + reserve;
        long rounded = (long)Math.Ceiling(need / (double)Rounding) * Rounding;
        int floor = (int)Math.Min(rounded, int.MaxValue);
        return modelContextLength > 0 ? Math.Min(floor, modelContextLength) : floor;
    }

    /// <summary>
    /// 这个上限是否可用（不低于 <see cref="MinimumFor"/>）
    /// </summary>
    /// <param name="usage">当前占用</param>
    /// <param name="modelContextLength">模型真实窗口</param>
    /// <param name="candidate">候选上限</param>
    /// <returns>可用为 true</returns>
    public static bool Fits(long usage, int modelContextLength, int candidate)
    {
        return candidate > 0 && candidate >= MinimumFor(usage, modelContextLength);
    }
}
