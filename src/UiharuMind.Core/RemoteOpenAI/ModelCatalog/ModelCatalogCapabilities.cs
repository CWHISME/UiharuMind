using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.RemoteOpenAI.ModelCatalog;

/// <summary>编辑窗口预填用的模型能力（预设 / 目录 / models.dev 三者合并后的结果）</summary>
/// <param name="IsVision">是否接受图片输入</param>
/// <param name="ContextLength">上下文窗口；0 表示未知</param>
/// <param name="MaxTokens">输出预算；0 表示未知</param>
/// <param name="OmitSamplingParams">是否不发送采样参数</param>
public readonly record struct ResolvedCapabilities(
    bool IsVision,
    int ContextLength,
    int MaxTokens,
    bool OmitSamplingParams);

/// <summary>
/// 目录条目 → 预填能力的合并。优先级：**预设变体（人工核对） &gt; 目录条目（实际端点声明）
/// &gt; 供应商官网（构建期抓取） &gt; models.dev（构建期外部元数据） &gt; models.dev 的 canonical 条目**。
///
/// 目录是实际在调的端点，比外部数据更贴近现实；预设是人工覆盖，压在最上面。
/// 官网与 models.dev 只在「目录没带该能力字段」时兜底（commandcode/opencode 的标准 /v1/models 只给 id）；
/// 两者之间官网赢——models.dev 对转售商缺行（commandcode 下 94 个模型只收了 72 个），
/// 缺一行不等于没能力，不兜就会把视觉判成 false。
/// 商汤目录的能力字段不可全信（MaxTokens / kimi-k3 的采样与视觉都与预设冲突），
/// 这类模型靠预设显式值压住；没进预设的模型按目录走。
/// </summary>
public static class ModelCatalogCapabilities
{
    /// <summary>
    /// 合并一条目录条目的能力。预设显式声明优先；否则目录；最后才是 models.dev。
    /// </summary>
    /// <param name="entry">目录条目（负责模型是否存在）</param>
    /// <param name="preset">该 ModelId 的预设变体；没有为 null</param>
    /// <param name="providerKey">models.dev 的 provider 键；空表示不查静态表</param>
    /// <returns>合并后的能力</returns>
    public static ResolvedCapabilities Resolve(ModelCatalogEntry entry, RemoteModelIdVariant? preset, string providerKey)
    {
        ModelCapabilityTable.Capability capability = default;
        bool hasTable = providerKey.Length > 0 &&
                        ModelCapabilityTable.TryGet(providerKey, entry.Id, out capability);
        // 供应商官网（权威、覆盖全）压过 models.dev（第三方快照，转售商常常缺行）
        ProviderPricingTable.PriceRow official = default;
        bool hasOfficial = providerKey.Length > 0 && ProviderPricingTable.TryGet(providerKey, entry.Id, out official);
        // 供应商自己的条目都没有（转售商快照落后，模型表里就是缺这一行）时走 canonical 兜底：
        // 缺一行不等于"没有能力"，不兜底就会把视觉之类的真能力判成 false
        ModelCapabilityTable.Canonical canonical = default;
        bool hasCanonical = !hasTable && !hasOfficial && ModelCapabilityTable.TryGetCanonical(entry.Id, out canonical);
        return new ResolvedCapabilities(
            // IsVision/OmitSamplingParams 是非空 bool，默认 false 无法区分「显式否」与「没声明」；
            // 只把 true 当显式声明，false 让目录/models.dev 继续判，避免默认值误压真实能力
            IsVision: preset is { IsVision: true } ? true
                : entry.InputModalities.Count > 0 ? entry.SupportsVision
                : hasOfficial ? official.IsVision
                : hasTable ? capability.IsVision
                : hasCanonical && canonical.IsVision,
            ContextLength: preset is { ContextLength: > 0 } presetContext
                ? presetContext.ContextLength
                : entry.ContextLength > 0 ? entry.ContextLength
                : hasOfficial && official.ContextLength > 0 ? official.ContextLength
                : hasTable ? capability.ContextLength
                : hasCanonical ? canonical.ContextLength : 0,
            MaxTokens: preset is { MaxTokens: > 0 } presetTokens
                ? presetTokens.MaxTokens
                : entry.MaxOutputLength > 0 ? entry.MaxOutputLength
                : hasTable ? capability.MaxTokens
                : hasCanonical ? canonical.MaxTokens : 0,
            OmitSamplingParams: preset is { OmitSamplingParams: true } ? true
                : entry.SupportedSamplingParameters.Count > 0 ? entry.OmitsSamplingParams
                : hasTable && capability.OmitSamplingParams);
    }

    /// <summary>配置类型 → models.dev provider 键；未知返回空串</summary>
    public static string ProviderKeyOf(Type configType) => ModelCapabilityTable.ProviderKeyOf(configType);
}
