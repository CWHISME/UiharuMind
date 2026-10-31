using UiharuMind.Core.AI.Models;

namespace UiharuMind.Core.RemoteOpenAI.ModelCatalog;

/// <summary>
/// models.dev 的一份<b>不可变</b>快照。更新即换引用（见 <see cref="ModelsDevCatalog"/>），
/// 读侧无锁——UI 线程与请求线程都会直接读它。
///
/// <para>空快照（<see cref="Empty"/>）代表「还没拉到」，所有查询都返回 false，
/// 由调用方退回构建期的静态表。不抛异常，也不区分「没拉过」与「拉失败」——
/// 那是界面上状态文案的事，不是数据层的事。</para>
/// </summary>
internal sealed class ModelsDevSnapshot
{
    private readonly Dictionary<string, ModelCapabilityTable.Capability> _capabilities; //键 provider/model
    private readonly Dictionary<string, ModelCapabilityTable.Canonical> _canonical; //键 canonical_model_id

    /// <summary>可用供应商（已滤掉无地址与模板地址的），按名称排序</summary>
    public IReadOnlyList<RemoteProviderInfo> Providers { get; }

    /// <summary>这份数据的拉取时间；空快照为 null</summary>
    public DateTimeOffset? FetchedAt { get; }

    /// <summary>条件请求用的 ETag</summary>
    public string? ETag { get; }

    /// <summary>没有任何供应商，即没拉到过</summary>
    public bool IsEmpty => Providers.Count == 0;

    /// <summary>空快照：还没拉到任何数据</summary>
    public static ModelsDevSnapshot Empty { get; } = new([], null, null,
        new Dictionary<string, ModelCapabilityTable.Capability>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, ModelCapabilityTable.Canonical>(StringComparer.OrdinalIgnoreCase));

    private ModelsDevSnapshot(
        IReadOnlyList<RemoteProviderInfo> providers,
        DateTimeOffset? fetchedAt,
        string? etag,
        Dictionary<string, ModelCapabilityTable.Capability> capabilities,
        Dictionary<string, ModelCapabilityTable.Canonical> canonical)
    {
        Providers = providers;
        FetchedAt = fetchedAt;
        ETag = etag;
        _capabilities = capabilities;
        _canonical = canonical;
    }

    /// <summary>按供应商与模型 ID 查能力（价格、窗口、思考档位都在这条记录里）</summary>
    /// <param name="providerKey">models.dev 的 provider 键</param>
    /// <param name="modelId">模型 ID</param>
    /// <param name="capability">查到的能力</param>
    public bool TryGetCapability(string providerKey, string modelId,
        out ModelCapabilityTable.Capability capability) =>
        _capabilities.TryGetValue(providerKey + "/" + modelId, out capability);

    /// <summary>按 <c>canonical_model_id</c> 查跨供应商兜底（只解「这是什么模型」，不解价格）</summary>
    /// <param name="modelId">模型 ID</param>
    /// <param name="canonical">查到的兜底信息</param>
    public bool TryGetCanonical(string modelId, out ModelCapabilityTable.Canonical canonical) =>
        _canonical.TryGetValue(modelId.Trim(), out canonical);

    /// <summary>把落盘格式读成快照</summary>
    /// <param name="file">快照文件内容</param>
    public static ModelsDevSnapshot FromCacheFile(ModelsDevCacheFile file)
    {
        List<RemoteProviderInfo> providers = new(file.Providers.Count);
        Dictionary<string, ModelCapabilityTable.Capability> capabilities =
            new(file.Providers.Sum(provider => provider.Models.Count), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ModelCapabilityTable.Canonical> canonical =
            new(file.Providers.Sum(provider => provider.Models.Count), StringComparer.OrdinalIgnoreCase);

        foreach (ModelsDevProviderDto provider in file.Providers)
        {
            providers.Add(new RemoteProviderInfo(provider.Id, provider.Name, provider.Api, provider.Doc,
                provider.Npm, provider.Models.Count));

            foreach (ModelsDevModelDto model in provider.Models)
            {
                capabilities[provider.Id + "/" + model.Id] = new ModelCapabilityTable.Capability(
                    model.IsVision,
                    model.MaxTokens,
                    model.ContextLength,
                    model.OmitSamplingParams,
                    model.SupportsToolCall,
                    model.IsReasoning,
                    model.Efforts,
                    (ThinkingControl)model.Thinking,
                    ToPrice(model));

                // 跨供应商兜底：同一 canonical 被多家收录时取并集/最大值，
                // 与生成器同一口径（一家转售商缺行时，别家那行仍能回答「这是什么模型」）
                if (string.IsNullOrWhiteSpace(model.CanonicalModelId)) continue;
                string key = model.CanonicalModelId.Trim();
                canonical[key] = canonical.TryGetValue(key, out ModelCapabilityTable.Canonical previous)
                    ? new ModelCapabilityTable.Canonical(
                        previous.IsVision || model.IsVision,
                        Math.Max(previous.ContextLength, model.ContextLength),
                        Math.Max(previous.MaxTokens, model.MaxTokens))
                    : new ModelCapabilityTable.Canonical(model.IsVision, model.ContextLength, model.MaxTokens);
            }
        }

        providers.Sort(static (left, right) =>
            string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase));

        return new ModelsDevSnapshot(providers, file.FetchedAt, file.ETag, capabilities, canonical);
    }

    private static ModelPrice ToPrice(ModelsDevModelDto model)
    {
        decimal[] cost = model.Cost;
        decimal[] tier = model.Tier;
        return new ModelPrice(
            At(cost, 0), At(cost, 1), At(cost, 2), At(cost, 3),
            model.TierFromContext,
            At(tier, 0), At(tier, 1), At(tier, 2), At(tier, 3));
    }

    private static decimal At(decimal[] values, int index) => index < values.Length ? values[index] : 0m;
}
