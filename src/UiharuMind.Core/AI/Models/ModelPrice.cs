using System.Globalization;

namespace UiharuMind.Core.AI.Models;

/// <summary>
/// 模型单价：<b>USD / 百万 token</b>。来自 models.dev 公布的目录价（见 <c>ModelCapabilityTable</c>），
/// 是"该供应商挂牌价"而非用户实际账单——订阅制/额度制的转售商按额度扣费，这个数只能算参考。
///
/// <see cref="IsKnown"/> 为 false 表示<b>供应商没公布</b>，不是免费；本地模型才是免费（不构造这个值）。
/// </summary>
/// <param name="Input">输入（未命中缓存）每百万 token 美元价</param>
/// <param name="Output">输出每百万 token 美元价</param>
/// <param name="CacheRead">命中前缀缓存的输入每百万 token 美元价，0 表示不单独计价</param>
/// <param name="CacheWrite">写入缓存每百万 token 美元价，0 表示不单独计价</param>
/// <param name="TierFromContext">阶梯起点（token 数），0 表示无阶梯价</param>
/// <param name="TierInput">超过阶梯起点后的输入价</param>
/// <param name="TierOutput">超过阶梯起点后的输出价</param>
/// <param name="TierCacheRead">超过阶梯起点后的缓存读价</param>
/// <param name="TierCacheWrite">超过阶梯起点后的缓存写价</param>
/// <param name="IsFree">供应商明写免费（如限时免费的预览模型）。与"没公布价格"是两回事：
/// 前者能算出 0，后者算不出，混成一个字段就会把免费模型显示成"未公布"</param>
public readonly record struct ModelPrice(
    decimal Input,
    decimal Output,
    decimal CacheRead,
    decimal CacheWrite,
    int TierFromContext,
    decimal TierInput,
    decimal TierOutput,
    decimal TierCacheRead,
    decimal TierCacheWrite,
    bool IsFree = false)
{
    /// <summary>没有价格（供应商未公布，或本地模型）</summary>
    public static ModelPrice Unknown { get; } = default;

    /// <summary>供应商是否公布了单价</summary>
    public bool IsKnown => IsFree || Input > 0 || Output > 0;

    /// <summary>是否有"超过某段上下文后涨价"的阶梯</summary>
    public bool HasTier => TierFromContext > 0 && (TierInput > 0 || TierOutput > 0);

    /// <summary>
    /// 按本次实际上下文长度取适用价：过了阶梯起点就换成阶梯价。
    /// 长会话（agent 跑十几轮工具往返能到几十万 token）不换价会低估成本
    /// </summary>
    /// <param name="contextTokens">本次请求的上下文 token 数</param>
    /// <returns>适用价；无阶梯或未过起点时返回自身</returns>
    public ModelPrice ForContext(long contextTokens)
    {
        if (!HasTier || contextTokens <= TierFromContext) return this;
        return this with
        {
            Input = TierInput > 0 ? TierInput : Input,
            Output = TierOutput > 0 ? TierOutput : Output,
            CacheRead = TierCacheRead > 0 ? TierCacheRead : CacheRead,
            CacheWrite = TierCacheWrite > 0 ? TierCacheWrite : CacheWrite,
            TierFromContext = 0,
        };
    }

    /// <summary>
    /// 算一笔账：命中缓存的输入按缓存价，其余按输入价，输出按输出价。单位美元。
    /// </summary>
    /// <param name="inputTokens">输入 token 总数（含命中）</param>
    /// <param name="cachedInputTokens">其中命中前缀缓存的</param>
    /// <param name="outputTokens">输出 token 数</param>
    /// <returns>这笔请求的美元花费；价格未知时为 0</returns>
    public decimal Cost(long inputTokens, long cachedInputTokens, long outputTokens)
    {
        if (!IsKnown) return 0;
        long cached = Math.Clamp(cachedInputTokens, 0, Math.Max(0, inputTokens));
        long uncached = Math.Max(0, inputTokens - cached);
        // 没单独给缓存价时按输入价算（宁可高估，也不要让人以为缓存不花钱）
        decimal cacheRead = CacheRead > 0 ? CacheRead : Input;
        return (uncached * Input + cached * cacheRead + outputTokens * Output) / 1_000_000m;
    }

    /// <summary>
    /// 单个价格的紧凑写法：<c>$0.15</c>、<c>$0.003</c>、<c>$15</c>；0 写 <c>—</c>（不公布而不是免费）
    /// </summary>
    /// <param name="value">美元的每百万 token 价</param>
    /// <returns>显示用文本</returns>
    public static string Format(decimal value) =>
        value > 0 ? "$" + value.ToString("0.####", CultureInfo.InvariantCulture) : "—";

    /// <summary>输入 / 输出 / 缓存读 三段式，列表行里一行放得下</summary>
    public string Summary => IsFree
        ? "free"
        : IsKnown
            ? $"in {Format(Input)} · out {Format(Output)} · cache {Format(CacheRead)}"
            : "";

    /// <summary>悬停明细：补上单位、缓存写与阶梯价。单位写死 USD/1M，不做汇率换算</summary>
    public string Tip
    {
        get
        {
            if (!IsKnown) return "";
            if (IsFree) return "USD per 1M tokens\nfree while the provider keeps it free";
            List<string> parts =
            [
                "USD per 1M tokens",
                $"in {Format(Input)}",
                $"out {Format(Output)}",
                $"cache read {Format(CacheRead)}",
            ];
            if (CacheWrite > 0) parts.Add($"cache write {Format(CacheWrite)}");
            if (HasTier) parts.Add($"over {TierFromContext / 1000}K ctx: in {Format(TierInput)} · out {Format(TierOutput)}");
            return string.Join("\n", parts);
        }
    }
}
