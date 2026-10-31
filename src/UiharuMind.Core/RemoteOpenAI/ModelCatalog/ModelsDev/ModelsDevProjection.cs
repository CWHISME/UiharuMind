namespace UiharuMind.Core.RemoteOpenAI.ModelCatalog;

/// <summary>
/// models.dev <c>api.json</c> → 快照的投影。
///
/// <para>规则与构建期的 <c>generate_modelsdev_capabilities.py</c> 一致（口径只能有一份）：
/// 视觉看 <c>modalities.input</c> 有没有 image；固定采样参数看 <c>temperature === false</c>；
/// 思考档位取 <c>reasoning_options</c> 里的取值表；价格是 USD 每百万 token，阶梯只取第一档。</para>
/// </summary>
internal static class ModelsDevProjection
{
    /// <summary>超过 20 万上下文才涨价的模型，models.dev 用老字段表达，没有显式起点</summary>
    private const int LegacyTierStart = 200_000;

    /// <summary>把原始数据投影成落盘格式（同时滤掉不能直接用的供应商）</summary>
    /// <param name="raw">原始 <c>api.json</c> 的供应商表</param>
    /// <param name="fetchedAt">拉取时间</param>
    /// <param name="etag">响应 ETag</param>
    public static ModelsDevCacheFile Build(
        Dictionary<string, ModelsDevRawProvider> raw,
        DateTimeOffset fetchedAt,
        string? etag)
    {
        ModelsDevCacheFile file = new()
        {
            SchemaVersion = ModelsDevCatalog.SchemaVersion,
            FetchedAt = fetchedAt,
            ETag = etag,
        };

        foreach ((string key, ModelsDevRawProvider provider) in raw)
        {
            // models.dev 只在自己配地址时写 api；地址编进 SDK 包的那几家（openai/groq/xai…）
            // 得靠这张小表补回来，否则最常用的几家全被滤掉
            string api = provider.Api is { Length: > 0 } declared
                ? declared
                : SdkEndpointFallbacks.Resolve(provider.Npm);

            // 还是没地址的走各家原生 SDK；地址里带 ${VAR} 的（按账户拼）填进表单也是废的
            if (api.Length == 0 || api.Contains("${", StringComparison.Ordinal)) continue;

            ModelsDevProviderDto dto = new()
            {
                Id = string.IsNullOrEmpty(provider.Id) ? key : provider.Id,
                Name = provider.Name ?? key,
                Api = api,
                Doc = provider.Doc ?? "",
                Npm = provider.Npm ?? "",
            };

            foreach ((string modelKey, ModelsDevRawModel model) in provider.Models ?? [])
            {
                dto.Models.Add(ProjectModel(string.IsNullOrEmpty(model.Id) ? modelKey : model.Id, model));
            }

            file.Providers.Add(dto);
        }

        return file;
    }

    private static ModelsDevModelDto ProjectModel(string id, ModelsDevRawModel model)
    {
        (decimal[] cost, int tierFrom, decimal[] tier) = ProjectPrice(model.Cost);
        return new ModelsDevModelDto
        {
            Id = id,
            IsVision = model.Modalities?.Input?.Contains("image") == true,
            ContextLength = model.Limit?.Context ?? 0,
            MaxTokens = model.Limit?.Output ?? 0,
            OmitSamplingParams = model.Temperature == false,
            SupportsToolCall = model.ToolCall == true,
            IsReasoning = model.Reasoning == true,
            Efforts = ProjectEfforts(model.ReasoningOptions),
            Thinking = (int)ProjectThinking(model.ReasoningOptions),
            Cost = cost,
            TierFromContext = tierFrom,
            Tier = tier,
            CanonicalModelId = model.CanonicalModelId,
        };
    }

    /// <summary>思考档位的取值表，按声明顺序去重</summary>
    private static string ProjectEfforts(List<ModelsDevRawReasoningOption>? options)
    {
        if (options is null || options.Count == 0) return "";

        List<string> values = [];
        foreach (ModelsDevRawReasoningOption option in options)
        {
            foreach (string value in option.Values ?? [])
            {
                if (!values.Contains(value)) values.Add(value);
            }
        }

        return string.Join(",", values);
    }

    /// <summary>
    /// 思考控制形态：有取值表按档位选（effort）、只有开关（toggle）、只给 token 预算（budget）。
    /// 混合声明时按「有没有档位可选」定，没声明是 Unknown——不等于不支持思考。
    /// </summary>
    private static ThinkingControl ProjectThinking(List<ModelsDevRawReasoningOption>? options)
    {
        if (options is null || options.Count == 0) return ThinkingControl.Unknown;

        bool hasToggle = false;
        bool hasBudget = false;
        foreach (ModelsDevRawReasoningOption option in options)
        {
            switch (option.Type)
            {
                case "effort": return ThinkingControl.Effort;
                case "toggle": hasToggle = true; break;
                case "budget": hasBudget = true; break;
            }
        }

        if (hasToggle) return ThinkingControl.Toggle;
        return hasBudget ? ThinkingControl.Budget : ThinkingControl.Unknown;
    }

    /// <summary>四价 + 第一档阶梯</summary>
    private static (decimal[] Cost, int TierFrom, decimal[] Tier) ProjectPrice(ModelsDevRawCost? cost)
    {
        decimal[] baseCost =
        [
            cost?.Input ?? 0m, cost?.Output ?? 0m, cost?.CacheRead ?? 0m, cost?.CacheWrite ?? 0m,
        ];

        if (cost?.Tiers is { Count: > 0 } tiers)
        {
            ModelsDevRawTier tier = tiers[0];
            return (baseCost, (int)(tier.Range?.Size ?? 0),
            [
                tier.Input ?? 0m, tier.Output ?? 0m, tier.CacheRead ?? 0m, tier.CacheWrite ?? 0m,
            ]);
        }

        if (cost?.ContextOver200K is { } over)
        {
            return (baseCost, LegacyTierStart,
            [
                over.Input ?? 0m, over.Output ?? 0m, over.CacheRead ?? 0m, over.CacheWrite ?? 0m,
            ]);
        }

        return (baseCost, 0, []);
    }
}
