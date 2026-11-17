/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.History;

/// <summary>
/// 图片总量预算的取值。<b>按字节</b>而不是按张数——真正让上游 400 的是请求体量，
/// 而字节天然把「一张大截图」和「十张小图」放在同一把尺子上；张数只当兜底（见 <see cref="MaxCount"/>）。
///
/// 回差（<see cref="TriggerBytes"/> 与 <see cref="TargetBytes"/> 之差）是这里唯一讲究的地方：
/// 每裁一次都要从被改的那条往后丢掉整段前缀缓存，所以不能让「每来一张新图」都推动裁剪——
/// 一次裁到停止线，之后得再攒够差额才动下一刀。同 <c>HistoryCompaction.InSteps</c> 的台阶，理由一模一样。
///
/// 两条线各自独立开关：把字节那条设成 0 不影响张数那条（反过来也一样），
/// 用户想只留张数上限时不必把两者一起关掉
/// </summary>
public readonly struct ImageBudgetLimits
{
    private const long MiB = 1024 * 1024;

    /// <summary>
    /// 建一个预算
    /// </summary>
    /// <param name="triggerBytes">越过这条线就裁（字节，按 base64 后的传输长度算）</param>
    /// <param name="targetBytes">一次裁到这条线以下（字节）</param>
    /// <param name="maxCount">张数硬顶；&lt;=0 表示不设</param>
    public ImageBudgetLimits(long triggerBytes, long targetBytes, int maxCount)
    {
        TriggerBytes = triggerBytes;
        TargetBytes = targetBytes;
        MaxCount = maxCount;
    }

    /// <summary>越过它就是该裁了</summary>
    public long TriggerBytes { get; }

    /// <summary>裁到它之下（必须小于 <see cref="TriggerBytes"/>，配反了会被夹到一半）</summary>
    public long TargetBytes { get; }

    /// <summary>
    /// 一次请求里最多留几张图。<b>纯兜底</b>：字节那一档正常会先触发，它只挡
    /// 「很多张很小的图」那种形状，以及按张数计费的网关
    /// </summary>
    public int MaxCount { get; }

    /// <summary>不裁</summary>
    public static ImageBudgetLimits Off => default;

    /// <summary>字节这条线开没开</summary>
    public bool UsesBytes => TriggerBytes > 0;

    /// <summary>张数这条线开没开</summary>
    public bool UsesCount => MaxCount > 0;

    /// <summary>两条都关着就什么都不做</summary>
    public bool IsOff => !UsesBytes && !UsesCount;

    /// <summary>
    /// 实际用于裁剪的停止线：配置写反了（target ≥ trigger）或没填时退到一半，
    /// 否则「越过 trigger 就一直丢」会在一发里把图丢干净
    /// </summary>
    public long EffectiveTarget => TargetBytes > 0 && TargetBytes < TriggerBytes ? TargetBytes : Math.Max(1, TriggerBytes / 2);

    /// <summary>
    /// 张数一次腾到哪儿：上限的四分之三。留出四分之一当回差，
    /// 否则每来一张新图都要推一次前缀（与字节那条同一个道理）
    /// </summary>
    public int EffectiveCountFloor => UsesCount ? Math.Max(1, MaxCount / 4 * 3) : 0;

    /// <summary>
    /// 从配置读一次。每次请求现读——用户改了阈值不必重启，与压缩水位现读模型同一条口径。
    ///
    /// <b>由组装层调用</b>（<c>AgentAssembler</c>），不放本类：这一层不该认识配置，
    /// 同层其它可变项（当前模型、上下文长度）也都是组装端取值再以委托传下来
    /// </summary>
    /// <param name="triggerMiB">触发线（MiB，&lt;=0 表示不按字节裁）</param>
    /// <param name="targetMiB">停止线（MiB，&lt;=0 表示按触发线的一半）</param>
    /// <param name="maxCount">张数上限（&lt;=0 表示不设）</param>
    /// <returns>预算</returns>
    public static ImageBudgetLimits From(int triggerMiB, int targetMiB, int maxCount) =>
        new(Math.Max(0, triggerMiB) * MiB, Math.Max(0, targetMiB) * MiB, maxCount);
}
