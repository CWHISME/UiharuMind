using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.RemoteOpenAI.ModelCatalog;

/// <summary>
/// 远程模型的能力与价格查询。<see cref="ModelCatalogCapabilities"/> 解的是「目录条目怎么预填」，
/// 这里解的是「一个已经存下来的模型是什么」——列表、会话、编辑窗口都从这里问。
///
/// 两份构建期数据，按权威度排序：<c>ProviderPricingTable</c>（供应商官网，权威但需要各自的抓取器）
/// 优先于 <c>ModelCapabilityTable</c>（models.dev，通用但会落后）。再往上压着预设表（人工核对），
/// 最底下是 canonical 兜底（前面都认不出这个模型时）。
/// </summary>
public static class ModelCapabilityResolver
{
    /// <summary>
    /// 该模型是否接受图片输入。优先级：目录声明 &gt; 预设 &gt; 官网 &gt; models.dev &gt; canonical。
    /// 谁都没有这个模型才判 false——转售商的目录快照常常落后，缺一行不等于没能力，
    /// 一律判 false 会让能用图的模型不能发图（图片会被直接丢掉）。
    /// </summary>
    /// <param name="config">远程模型配置</param>
    /// <param name="catalogVision">目录条目自己的判断；目录没声明该字段时传 null</param>
    /// <returns>是否视觉模型</returns>
    public static bool ResolveVision(BaseRemoteModelConfig config, bool? catalogVision = null)
    {
        if (catalogVision is { } declared) return declared;
        if (config.ModelIdVariants.TryGetValue(config.ModelId, out RemoteModelIdVariant? preset) && preset.IsVision)
            return true;

        string providerKey = ModelCapabilityTable.ProviderKeyOf(config.GetType());
        if (providerKey.Length > 0)
        {
            if (ProviderPricingTable.TryGet(providerKey, config.ModelId, out var official)) return official.IsVision;
            if (ModelCapabilityTable.TryGet(providerKey, config.ModelId, out var capability)) return capability.IsVision;
        }

        // 自定义服务商（没有 provider 键）与转售商缺行都落这里
        return ModelCapabilityTable.TryGetCanonical(config.ModelId, out var canonical) && canonical.IsVision;
    }

    /// <summary>
    /// 模型的挂牌单价（USD / 百万 token）。<b>只查该供应商自己的条目</b>：
    /// canonical 兜底能回答"这是什么模型"，但价格是每个转售商各挂各的，跨家借价等于编。
    /// </summary>
    /// <param name="config">远程模型配置</param>
    /// <param name="price">查到的单价</param>
    /// <returns>该供应商公布了单价为 true</returns>
    public static bool TryGetPrice(BaseRemoteModelConfig config, out ModelPrice price) =>
        TryGetPrice(config.GetType(), config.ModelId, out price);

    /// <summary>
    /// 按配置类型与模型 ID 查挂牌单价（编辑窗口在候选列表里逐条查，没有配置实例）。
    /// 官网覆盖层优先——models.dev 对转售商缺行的地方，官网一定有。
    /// </summary>
    /// <param name="configType">远程配置类型</param>
    /// <param name="modelId">模型 ID</param>
    /// <param name="price">查到的单价</param>
    /// <returns>该供应商公布了单价为 true</returns>
    public static bool TryGetPrice(Type configType, string modelId, out ModelPrice price)
    {
        price = ModelPrice.Unknown;
        string providerKey = ModelCapabilityTable.ProviderKeyOf(configType);
        if (providerKey.Length == 0) return false;

        if (ProviderPricingTable.TryGet(providerKey, modelId, out var official) && official.Price.IsKnown)
        {
            price = official.Price;
            return true;
        }

        if (!ModelCapabilityTable.TryGet(providerKey, modelId, out var capability) || !capability.Price.IsKnown)
        {
            return false;
        }

        price = capability.Price;
        return true;
    }

    /// <summary>
    /// 该模型声明支持的思考档位（如 <c>low,high,max</c>）。
    /// 服务端不认的档位可能被静默降级或直接报错，编辑窗口据此提示；
    /// 空串表示没声明（不代表不支持思考）。
    /// </summary>
    /// <param name="config">远程模型配置</param>
    /// <returns>逗号分隔的档位；未声明为空串</returns>
    public static string ResolveEfforts(BaseRemoteModelConfig config) =>
        ResolveEfforts(config.GetType(), config.ModelId);

    /// <summary>
    /// 按配置类型与模型 ID 查声明的思考档位
    /// </summary>
    /// <param name="configType">远程配置类型</param>
    /// <param name="modelId">模型 ID</param>
    /// <returns>逗号分隔的档位；未声明为空串</returns>
    public static string ResolveEfforts(Type configType, string modelId)
    {
        string providerKey = ModelCapabilityTable.ProviderKeyOf(configType);
        return providerKey.Length > 0 && ModelCapabilityTable.TryGet(providerKey, modelId, out var capability)
            ? capability.Efforts
            : "";
    }
}
