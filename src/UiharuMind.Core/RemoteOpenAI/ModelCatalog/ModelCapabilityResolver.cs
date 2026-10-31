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

        string providerKey = ProviderKeyOf(config);
        if (providerKey.Length > 0)
        {
            if (ProviderPricingTable.TryGet(providerKey, config.ModelId, out var official)) return official.IsVision;
            if (ModelCapabilitySource.TryGet(providerKey, config.ModelId, out var capability)) return capability.IsVision;
        }

        // 自定义服务商（没有 provider 键）与转售商缺行都落这里
        return ModelCapabilitySource.TryGetCanonical(config.ModelId, out var canonical) && canonical.IsVision;
    }

    /// <summary>
    /// 模型的挂牌单价（USD / 百万 token）。<b>只查该供应商自己的条目</b>：
    /// canonical 兜底能回答"这是什么模型"，但价格是每个转售商各挂各的，跨家借价等于编。
    /// </summary>
    /// <param name="config">远程模型配置</param>
    /// <param name="price">查到的单价</param>
    /// <returns>该供应商公布了单价为 true</returns>
    public static bool TryGetPrice(BaseRemoteModelConfig config, out ModelPrice price) =>
        TryGetPrice(ProviderKeyOf(config), config.ModelId, out price);

    /// <summary>
    /// 按配置类型与模型 ID 查挂牌单价（编辑窗口在候选列表里逐条查，没有配置实例）。
    /// 官网覆盖层优先——models.dev 对转售商缺行的地方，官网一定有。
    /// </summary>
    /// <param name="configType">远程配置类型</param>
    /// <param name="modelId">模型 ID</param>
    /// <param name="price">查到的单价</param>
    /// <returns>该供应商公布了单价为 true</returns>
    public static bool TryGetPrice(Type configType, string modelId, out ModelPrice price) =>
        TryGetPrice(RemoteModelConfigRegistry.ProviderKeyOf(configType), modelId, out price);

    /// <summary>
    /// 按 provider 键与模型 ID 查挂牌单价（编辑窗口里逐条查候选时用——那时手上只有键，没有实例）
    /// </summary>
    /// <param name="providerKey">models.dev 的 provider 键；空表示查不到</param>
    /// <param name="modelId">模型 ID</param>
    /// <param name="price">查到的单价</param>
    /// <returns>该供应商公布了单价为 true</returns>
    public static bool TryGetPrice(string providerKey, string modelId, out ModelPrice price)
    {
        price = ModelPrice.Unknown;
        if (providerKey.Length == 0) return false;

        if (ProviderPricingTable.TryGet(providerKey, modelId, out var official) && official.Price.IsKnown)
        {
            price = official.Price;
            return true;
        }

        if (!ModelCapabilitySource.TryGet(providerKey, modelId, out var capability) || !capability.Price.IsKnown)
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
        ResolveEfforts(ProviderKeyOf(config), config.ModelId);

    /// <summary>
    /// 按配置类型与模型 ID 查声明的思考档位
    /// </summary>
    /// <param name="configType">远程配置类型</param>
    /// <param name="modelId">模型 ID</param>
    /// <returns>逗号分隔的档位；未声明为空串</returns>
    public static string ResolveEfforts(Type configType, string modelId) =>
        ResolveEfforts(RemoteModelConfigRegistry.ProviderKeyOf(configType), modelId);

    /// <summary>按 provider 键与模型 ID 查声明的思考档位</summary>
    /// <param name="providerKey">models.dev 的 provider 键；空表示不查</param>
    /// <param name="modelId">模型 ID</param>
    /// <returns>逗号分隔的档位；未声明为空串</returns>
    public static string ResolveEfforts(string providerKey, string modelId) =>
        providerKey.Length > 0 && ModelCapabilitySource.TryGet(providerKey, modelId, out var capability)
            ? capability.Efforts
            : "";

    /// <summary>
    /// 该模型可选哪些思考档位（编辑窗口下拉的候选）。
    /// <see cref="ThinkingProfile.Levels"/> 为空表示这个模型根本没有档位——只有开关，或只认 token 预算。
    /// </summary>
    /// <param name="config">远程模型配置</param>
    /// <returns>档位候选与「模型是否表过态」</returns>
    public static ThinkingProfile ResolveThinkingProfile(BaseRemoteModelConfig config) =>
        BuildThinkingProfile(ProviderKeyOf(config), config.ModelId);

    /// <summary>
    /// 按配置类型与模型 ID 取思考档位候选
    /// </summary>
    /// <param name="configType">远程配置类型</param>
    /// <param name="modelId">模型 ID</param>
    /// <returns>档位候选与「模型是否表过态」</returns>
    public static ThinkingProfile ResolveThinkingProfile(Type configType, string modelId) =>
        BuildThinkingProfile(RemoteModelConfigRegistry.ProviderKeyOf(configType), modelId);

    private static ThinkingProfile BuildThinkingProfile(string providerKey, string modelId) =>
        ResolveThinkingControl(providerKey, modelId) switch
        {
            // 只有开关或只有 token 预算：没有档位名可选
            ThinkingControl.Toggle or ThinkingControl.Budget => new ThinkingProfile([], true),
            ThinkingControl.Effort => new ThinkingProfile(
                ThinkingEfforts.ParseDeclared(ResolveEfforts(providerKey, modelId)), true),
            // 没声明不等于不支持思考，给兜底阶梯；但「没表态」要让界面知道，
            // 否则用户自己填过的档位会被当成不支持而清掉
            _ => new ThinkingProfile(ThinkingEfforts.Fallback, false),
        };

    private static ThinkingControl ResolveThinkingControl(string providerKey, string modelId) =>
        providerKey.Length > 0 && ModelCapabilitySource.TryGet(providerKey, modelId, out var capability)
            ? capability.Thinking
            : ThinkingControl.Unknown;

    /// <summary>
    /// 该配置对应哪个 models.dev 供应商键：<b>实例上的键优先</b>（经「更多供应商」选的自定义配置），
    /// 否则按配置类型查注册表。这条口径决定了从 models.dev 选的供应商能不能查到价格与思考档位。
    /// </summary>
    /// <param name="config">远程模型配置</param>
    public static string ProviderKeyOf(BaseRemoteModelConfig config) =>
        config.CatalogProviderKey is { Length: > 0 } key
            ? key
            : RemoteModelConfigRegistry.ProviderKeyOf(config.GetType());
}
