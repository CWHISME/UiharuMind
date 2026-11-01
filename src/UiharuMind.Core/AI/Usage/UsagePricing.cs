using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Status;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.RemoteOpenAI.ModelCatalog;

namespace UiharuMind.Core.AI.Usage;

/// <summary>
/// 被计价的那一次请求的价格快照。四个价与倍率都留证：价格表是生成物，
/// 明天重跑脚本价格就变了，不存快照的话历史账单会跟着变（用户会以为是 bug）
/// </summary>
/// <param name="CostUsd">这笔请求的估算费用；null = 未定价</param>
/// <param name="Source"><c>official</c> / <c>modelsdev</c>；未定价为 null</param>
/// <param name="Input">计价用的输入价（USD / 百万 token）</param>
/// <param name="Output">输出价</param>
/// <param name="CacheRead">缓存读价；供应商没公布时为 null（NULL ≠ 0）</param>
/// <param name="Multiplier">峰时/促销倍率</param>
public readonly record struct UsagePriceSnapshot(
    decimal? CostUsd,
    string? Source,
    decimal? Input,
    decimal? Output,
    decimal? CacheRead,
    decimal Multiplier)
{
    /// <summary>没有价格（本地模型、供应商未公布）</summary>
    public static UsagePriceSnapshot Unpriced { get; } = new(null, null, null, null, null, 1m);
}

/// <summary>
/// 一次请求的模型身份快照 + 基础单价。模型会改配置、会被删，历史记录必须回答
/// 「当时用的是哪个模型」——所以这几个字段在请求开始时就定格，不 join 配置
/// </summary>
/// <param name="ModelName">用户可见名（配置字典键）</param>
/// <param name="ModelId">发给服务端的 id</param>
/// <param name="ProviderId">配置类型名；本地为 <c>local</c></param>
/// <param name="ProviderKey">定价表键</param>
/// <param name="ApiBase">归一化 base url</param>
/// <param name="IsLocal">是否本地模型</param>
/// <param name="Price">该供应商公布的基础价（未经阶梯换算）；null 表示未定价</param>
/// <param name="PriceSource">价格来源</param>
/// <param name="Multiplier">峰时/促销倍率</param>
public sealed record UsageModelIdentity(
    string ModelName,
    string ModelId,
    string ProviderId,
    string ProviderKey,
    string ApiBase,
    bool IsLocal,
    ModelPrice? Price,
    string? PriceSource,
    decimal Multiplier)
{
    /// <summary>解析不出模型（例如请求在拿到模型前就失败）</summary>
    public static UsageModelIdentity Unknown { get; } = new("", "", "", "", "", false, null, null, 1m);

    /// <summary>
    /// 从「真正会发请求的那个模型」做一份身份快照。纯函数：只读配置，不碰网络、不拉起模型
    /// </summary>
    /// <param name="model">发请求的模型；null 记为空身份</param>
    /// <param name="now">当前时刻（峰时倍率按时钟算）</param>
    /// <returns>身份快照</returns>
    public static UsageModelIdentity From(ModelRunningData? model, DateTimeOffset now)
    {
        if (model == null) return Unknown;

        ILlmModel info = model.ModelInfo;
        //本地模型不构造 ModelPrice：免费不是"有价"（见 ModelPrice 的注释）
        if (info is not RemoteModelInfo remote) return new UsageModelIdentity(model.ModelName, info.ModelId, "local",
            "", "", true, null, null, 1m);

        BaseRemoteModelConfig config = remote.Config;
        string providerKey = ModelCapabilityResolver.ProviderKeyOf(config);
        UsagePricing.TryResolveBasePrice(providerKey, config.ModelId, out ModelPrice price, out string? source);
        //归一化 base 只算一次：身份快照与峰时规则用的是同一个值
        string apiBase = ProviderStatusContext.DeriveBaseUrl(config.ModelPath);
        decimal multiplier = UsagePricing.PeakMultiplier(config, now, apiBase);
        return new UsageModelIdentity(model.ModelName, config.ModelId, remote.ProviderId, providerKey,
            apiBase, false, price.IsKnown ? price : null, source, multiplier);
    }

    /// <summary>
    /// 把这笔用量换算成价格快照：阶梯价按本次上下文长度换档，再乘峰时倍率
    /// </summary>
    /// <param name="inputTokens">输入 token（含命中缓存的部分）</param>
    /// <param name="cachedRead">其中命中前缀缓存的</param>
    /// <param name="outputTokens">输出 token</param>
    /// <returns>价格快照</returns>
    public UsagePriceSnapshot PriceFor(long inputTokens, long cachedRead, long outputTokens)
    {
        if (IsLocal || Price is not { } price) return UsagePriceSnapshot.Unpriced;

        ModelPrice applied = price.ForContext(inputTokens);
        if (!applied.IsKnown) return UsagePriceSnapshot.Unpriced;

        decimal cost = applied.Cost(inputTokens, cachedRead, outputTokens) * Multiplier;
        return new UsagePriceSnapshot(
            cost,
            PriceSource,
            applied.Input,
            applied.Output,
            //缓存价没公布时存 NULL：UI 据此把"缓存的节省"记 0，而不是虚报成一整份输入价
            applied.CacheRead > 0 ? applied.CacheRead : null,
            Multiplier);
    }
}

/// <summary>
/// 计价：复用 <see cref="ModelPrice"/>，不新写公式。
/// <para>
/// 口径（页面上也写这一行）：<c>cost = (未命中输入 × 输入价 + 命中输入 × 缓存读价 + 输出 × 输出价) / 1,000,000</c>，
/// 单位 USD / 百万 token。缓存读价未公布时按输入价计（宁可高估，也不让人以为缓存不花钱）；
/// 阶梯价在上下文超过 <c>TierFromContext</c> 时整笔换用阶梯价；不跨供应商借价
/// </para>
/// </summary>
public static class UsagePricing
{
    /// <summary>价格来源之一：供应商官网定价页（权威）</summary>
    public const string SourceOfficial = "official";

    /// <summary>价格来源之一：models.dev（通用但会落后）</summary>
    public const string SourceModelsDev = "modelsdev";

    /// <summary>
    /// 查这个供应商 + 模型的挂牌价，并给出<b>来源</b>。
    /// 优先级只有一份实现（<see cref="ModelCapabilityResolver.TryGetPrice(string,string,out ModelPrice,out bool)"/>）：
    /// 官网 &gt; models.dev，不跨供应商借价。价格表是生成物，两份实现漂移一天就是历史账单错
    /// </summary>
    /// <param name="providerKey">定价表键；空表示查不到</param>
    /// <param name="modelId">模型 ID</param>
    /// <param name="price">查到的价；没查到为 <see cref="ModelPrice.Unknown"/></param>
    /// <param name="source">来源常量；没查到为 null</param>
    /// <returns>该供应商公布了单价返回 true</returns>
    public static bool TryResolveBasePrice(string providerKey, string modelId, out ModelPrice price,
        out string? source)
    {
        if (!ModelCapabilityResolver.TryGetPrice(providerKey, modelId, out price, out bool fromOfficial))
        {
            source = null;
            return false;
        }

        source = fromOfficial ? SourceOfficial : SourceModelsDev;
        return true;
    }

    /// <summary>
    /// 此刻该模型适用的峰时/促销倍率（只取高峰计价那一档；促销加倍额度不是涨价）。
    /// 规则是纯本地按时钟算的（<see cref="IModelBillingRule"/>），零网络
    /// </summary>
    /// <param name="config">远程模型配置</param>
    /// <param name="now">当前时刻</param>
    /// <param name="apiBase">归一化 base url（调用方已算好的那个，不在这里重复推导）</param>
    /// <returns>倍率；没有规则或不在窗口内为 1</returns>
    public static decimal PeakMultiplier(BaseRemoteModelConfig config, DateTimeOffset now, string apiBase)
    {
        Type configType = config.GetType();
        if (RemoteModelConfigRegistry.ResolveConfigType(config.ConfigType) is { } resolved) configType = resolved;
        if (ProviderStatusRegistry.For(configType)?.Rule is not { } rule) return 1m;

        //规则只读 ModelId；密钥不参与，也不必把明文往这里带
        ProviderStatusContext context = new(configType, apiBase,
            string.Empty, config.ModelId, config.ModelName);
        double multiplier = 1;
        foreach (ProviderStatusNotice notice in rule.Describe(context, now))
        {
            if (notice.Kind == EProviderNoticeKind.PeakPricing) multiplier = notice.Multiplier;
        }

        return (decimal)multiplier;
    }

    /// <summary>
    /// 缓存节省 = <c>cached_read × (输入价 − 缓存读价) / 1e6</c>；缓存价未公布时记 0（宁不高估）
    /// </summary>
    /// <param name="cachedRead">命中缓存的输入 token</param>
    /// <param name="inputPrice">输入价（USD / 百万 token）</param>
    /// <param name="cacheReadPrice">缓存读价；null 表示未公布</param>
    /// <returns>节省的美元数；算不出为 0</returns>
    public static decimal CacheSaved(long cachedRead, decimal? inputPrice, decimal? cacheReadPrice)
    {
        if (cachedRead <= 0 || inputPrice is not { } input || cacheReadPrice is not { } cachePrice) return 0m;
        return cachedRead * (input - cachePrice) / 1_000_000m;
    }
}
