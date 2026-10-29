using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.RemoteOpenAI.ModelCatalog;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 静态能力表的两类查询：转售商目录缺行时的 canonical 兜底（视觉被误判成 false 会让图片发不出去），
/// 以及挂牌单价与阶梯价。用 models.dev 里真实存在的条目做锚点。
/// </summary>
public class ModelCapabilityResolverTests
{
    /// <summary>
    /// 转售商目录里没有这一行（CommandCode 下 94 个模型 models.dev 只收了 72 个），
    /// 按供应商官网的覆盖层认出它是视觉模型，并给出官网挂牌价
    /// </summary>
    [Fact]
    public void Resolve_ResellerCatalogMissingModel_UsesOfficialTable()
    {
        var entry = new ModelCatalogEntry { Id = "xiaomi/mimo-v2.6-pro" };

        ResolvedCapabilities resolved = ModelCatalogCapabilities.Resolve(entry, null, "commandcode");

        Assert.True(resolved.IsVision);
        Assert.True(resolved.ContextLength > 0);
    }

    /// <summary>没有 provider 键的自定义端点仍走 canonical 兜底</summary>
    [Fact]
    public void Resolve_CustomProvider_FallsBackToCanonical()
    {
        var entry = new ModelCatalogEntry { Id = "xiaomi/mimo-v2.6-pro" };

        ResolvedCapabilities resolved = ModelCatalogCapabilities.Resolve(entry, null, "");

        Assert.True(resolved.IsVision);
    }

    /// <summary>哪都没有的模型才判 false</summary>
    [Fact]
    public void Resolve_UnknownEverywhere_IsNotVision()
    {
        var entry = new ModelCatalogEntry { Id = "local/definitely-not-a-model" };

        ResolvedCapabilities resolved = ModelCatalogCapabilities.Resolve(entry, null, "commandcode");

        Assert.False(resolved.IsVision);
        Assert.Equal(0, resolved.ContextLength);
    }

    /// <summary>供应商自己有这一行就听它的，不再走 canonical</summary>
    [Fact]
    public void Resolve_ProviderEntryWins()
    {
        var entry = new ModelCatalogEntry { Id = "deepseek-flash" };

        ResolvedCapabilities resolved = ModelCatalogCapabilities.Resolve(entry, null, "deepseek");

        Assert.True(resolved.IsVision);
        Assert.Equal(1048576, resolved.ContextLength);
    }

    /// <summary>
    /// 视觉是派生值：显式设置能压住推导，清掉覆盖后回到推导
    /// （老存档里被存成 false 的模型因此能在下一次加载自动校正）
    /// </summary>
    [Fact]
    public void Vision_OverrideBeatsDerived_AndClearingRestoresIt()
    {
        var config = new RemoteDeepSeekModelConfig { ModelId = "deepseek-flash" };
        Assert.True(config.IsVision);

        config.IsVision = false;
        Assert.False(config.IsVision);

        config.IsVisionOverride = null;
        Assert.True(config.IsVision);
    }

    /// <summary>DeepSeek 公布的三项价</summary>
    [Fact]
    public void Price_KnownProvider()
    {
        var config = new RemoteDeepSeekModelConfig { ModelId = "deepseek-flash" };

        Assert.True(ModelCapabilityResolver.TryGetPrice(config, out ModelPrice price));
        Assert.Equal(0.15m, price.Input);
        Assert.Equal(0.6m, price.Output);
        Assert.Equal(0.003m, price.CacheRead);
        Assert.False(price.HasTier);
    }

    /// <summary>阶梯价：过了起点就换价，长会话不该按基础价估（opencode 只有 models.dev 一份数据）</summary>
    [Fact]
    public void Price_TieredModel_SwitchesOverThreshold()
    {
        var config = new RemoteOpenCodeModelConfig { ModelId = "claude-sonnet-4-5" };

        Assert.True(ModelCapabilityResolver.TryGetPrice(config, out ModelPrice price));
        Assert.True(price.HasTier);

        ModelPrice small = price.ForContext(50_000);
        Assert.Equal(3m, small.Input);

        ModelPrice large = price.ForContext(400_000);
        Assert.Equal(6m, large.Input);
        Assert.False(large.HasTier);
    }

    /// <summary>官网覆盖层给出的价（models.dev 根本没有这三行）</summary>
    [Fact]
    public void Price_FromOfficialTable_WhereModelsDevHasNoRow()
    {
        var config = new RemoteCommandCodeModelConfig { ModelId = "xiaomi/mimo-v2.6-pro" };

        Assert.True(ModelCapabilityResolver.TryGetPrice(config, out ModelPrice price));
        Assert.Equal(0.435m, price.Input);
        Assert.Equal(0.87m, price.Output);
        Assert.Equal(0.0036m, price.CacheRead);
    }

    /// <summary>官网明写免费的模型：能算出 0，而不是"未公布"</summary>
    [Fact]
    public void Price_FreeModelIsKnownButFree()
    {
        var config = new RemoteCommandCodeModelConfig { ModelId = "poolside/laguna-s-2.1-free" };

        Assert.True(ModelCapabilityResolver.TryGetPrice(config, out ModelPrice price));
        Assert.True(price.IsKnown);
        Assert.True(price.IsFree);
        Assert.Equal("free", price.Summary);
        Assert.Equal(0, price.Cost(1_000_000, 0, 1_000_000));
    }

    /// <summary>自定义服务商没有 provider 键，不猜价</summary>
    [Fact]
    public void Price_UnknownForCustomProvider()
    {
        var config = new RemoteModelConfig { ModelId = "deepseek-flash" };

        Assert.False(ModelCapabilityResolver.TryGetPrice(config, out _));
    }

    /// <summary>命中缓存的输入按缓存价算，其余按输入价</summary>
    [Fact]
    public void Price_CostUsesCachePriceForCachedInput()
    {
        var price = new ModelPrice(1m, 2m, 0.1m, 0m, 0, 0m, 0m, 0m, 0m);

        decimal cost = price.Cost(inputTokens: 1_000_000, cachedInputTokens: 400_000, outputTokens: 1_000_000);

        Assert.Equal(0.6m + 0.04m + 2m, cost);
    }

    /// <summary>没公布缓存价时按输入价算，宁可高估也不假装缓存免费</summary>
    [Fact]
    public void Price_CostFallsBackToInputPriceWithoutCachePrice()
    {
        var price = new ModelPrice(1m, 2m, 0m, 0m, 0, 0m, 0m, 0m, 0m);

        Assert.Equal(1m, price.Cost(inputTokens: 1_000_000, cachedInputTokens: 500_000, outputTokens: 0));
    }

    /// <summary>价格未公布不等于免费</summary>
    [Fact]
    public void Price_UnknownIsNotFree()
    {
        Assert.False(ModelPrice.Unknown.IsKnown);
        Assert.Equal("", ModelPrice.Unknown.Summary);
        Assert.Equal(0, ModelPrice.Unknown.Cost(1_000_000, 0, 1_000_000));
    }
}
