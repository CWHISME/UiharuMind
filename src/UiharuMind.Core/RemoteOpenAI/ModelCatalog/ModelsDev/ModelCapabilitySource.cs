namespace UiharuMind.Core.RemoteOpenAI.ModelCatalog;

/// <summary>
/// 能力查询的单一入口：<b>运行时快照优先，构建期静态表兜底</b>。
///
/// <para>两层各管一段（ADR 0103）：快照是 models.dev 的最新数据，覆盖全部可用供应商；
/// 静态表是随版本发布的旧快照，只为「首次启动且没网」时仍能判视觉——视觉是 <c>bool</c>，
/// 没有「未知」这一档，空表等于把能看图的模型判成不能看，图片会被直接丢掉（ADR 0097 的原案）。</para>
///
/// <para>官网价格表（<c>ProviderPricingTable</c>）不在这里：它压在这两层之上，
/// 由 <see cref="ModelCapabilityResolver"/> 先问。</para>
/// </summary>
internal static class ModelCapabilitySource
{
    /// <summary>按供应商与模型 ID 查能力</summary>
    /// <param name="providerKey">models.dev 的 provider 键；空表示不查</param>
    /// <param name="modelId">模型 ID</param>
    /// <param name="capability">查到的能力</param>
    public static bool TryGet(string providerKey, string modelId,
        out ModelCapabilityTable.Capability capability)
    {
        if (providerKey.Length == 0)
        {
            capability = default;
            return false;
        }

        return ModelsDevCatalog.Instance.Current.TryGetCapability(providerKey, modelId, out capability) ||
               ModelCapabilityTable.TryGet(providerKey, modelId, out capability);
    }

    /// <summary>按 <c>canonical_model_id</c> 查跨供应商兜底</summary>
    /// <param name="modelId">模型 ID</param>
    /// <param name="canonical">查到的兜底信息</param>
    public static bool TryGetCanonical(string modelId, out ModelCapabilityTable.Canonical canonical) =>
        ModelsDevCatalog.Instance.Current.TryGetCanonical(modelId, out canonical) ||
        ModelCapabilityTable.TryGetCanonical(modelId, out canonical);
}
