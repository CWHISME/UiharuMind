using System.Text.Json.Serialization;

namespace UiharuMind.Core.RemoteOpenAI.ModelCatalog;

/// <summary>
/// models.dev 快照的落盘格式（<c>Cache/ModelsDev/snapshot.json</c>）。
///
/// <para>存的是<b>投影</b>而不是原始 <c>api.json</c>：原始 5.7 MB，只留我们用到的字段后约 0.7 MB，
/// 解析也快一个量级。代价是 models.dev 加了新字段要等下次拉取才拿到——可接受，本来就要拉。
/// 属性名一律短名：7000 多个模型，字段名占的体积比数据本身还多。</para>
///
/// <para><see cref="SchemaVersion"/> 变了直接丢弃旧文件重拉，避免投影字段调整后读到错位的旧结构。</para>
/// </summary>
internal sealed class ModelsDevCacheFile
{
    /// <summary>投影格式版本，改动字段含义时 +1</summary>
    [JsonPropertyName("schema")]
    public int SchemaVersion { get; set; }

    /// <summary>这份数据的拉取时间（界面上「3 天前更新」用它）</summary>
    [JsonPropertyName("fetchedAt")]
    public DateTimeOffset FetchedAt { get; set; }

    /// <summary>上次响应的 ETag，手动刷新时走条件请求，没变就 304</summary>
    [JsonPropertyName("etag")]
    public string? ETag { get; set; }

    /// <summary>可用供应商（已滤掉无地址与模板地址的）</summary>
    [JsonPropertyName("providers")]
    public List<ModelsDevProviderDto> Providers { get; set; } = [];
}

/// <summary>快照里的一家供应商</summary>
internal sealed class ModelsDevProviderDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";

    [JsonPropertyName("name")] public string Name { get; set; } = "";

    [JsonPropertyName("api")] public string Api { get; set; } = "";

    [JsonPropertyName("doc")] public string Doc { get; set; } = "";

    [JsonPropertyName("npm")] public string Npm { get; set; } = "";

    [JsonPropertyName("models")] public List<ModelsDevModelDto> Models { get; set; } = [];
}

/// <summary>快照里的一个模型</summary>
internal sealed class ModelsDevModelDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";

    /// <summary>接受图片输入</summary>
    [JsonPropertyName("vision")] public bool IsVision { get; set; }

    /// <summary>上下文窗口，0 表示没声明</summary>
    [JsonPropertyName("ctx")] public int ContextLength { get; set; }

    /// <summary>输出预算，0 表示没声明</summary>
    [JsonPropertyName("out")] public int MaxTokens { get; set; }

    /// <summary>固定采样参数（temperature 等不能显式传）</summary>
    [JsonPropertyName("omit")] public bool OmitSamplingParams { get; set; }

    [JsonPropertyName("tool")] public bool SupportsToolCall { get; set; }

    [JsonPropertyName("reason")] public bool IsReasoning { get; set; }

    /// <summary>声明的思考档位，逗号分隔；空表示没声明</summary>
    [JsonPropertyName("efforts")] public string Efforts { get; set; } = "";

    /// <summary>思考控制形态，<see cref="ThinkingControl"/> 的序号</summary>
    [JsonPropertyName("thinking")] public int Thinking { get; set; }

    /// <summary>四价：输入 / 输出 / 缓存读 / 缓存写（USD 每百万 token）</summary>
    [JsonPropertyName("cost")] public decimal[] Cost { get; set; } = [];

    /// <summary>阶梯价起点（token 数），0 表示无阶梯</summary>
    [JsonPropertyName("tierFrom")] public int TierFromContext { get; set; }

    /// <summary>阶梯价四价</summary>
    [JsonPropertyName("tier")] public decimal[] Tier { get; set; } = [];

    /// <summary>跨供应商的规范模型 ID，用于「这家目录缺行」时认出这是什么模型</summary>
    [JsonPropertyName("canonical")] public string? CanonicalModelId { get; set; }
}

/// <summary>models.dev <c>api.json</c> 的一家供应商（只声明用到的字段，其余由 STJ 跳过）</summary>
internal sealed class ModelsDevRawProvider
{
    [JsonPropertyName("id")] public string? Id { get; set; }

    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("api")] public string? Api { get; set; }

    [JsonPropertyName("doc")] public string? Doc { get; set; }

    [JsonPropertyName("npm")] public string? Npm { get; set; }

    [JsonPropertyName("models")] public Dictionary<string, ModelsDevRawModel>? Models { get; set; }
}

/// <summary>models.dev <c>api.json</c> 的一个模型</summary>
internal sealed class ModelsDevRawModel
{
    [JsonPropertyName("id")] public string? Id { get; set; }

    [JsonPropertyName("cost")] public ModelsDevRawCost? Cost { get; set; }

    [JsonPropertyName("limit")] public ModelsDevRawLimit? Limit { get; set; }

    [JsonPropertyName("modalities")] public ModelsDevRawModalities? Modalities { get; set; }

    /// <summary>为 <c>false</c> 表示该模型拒绝显式采样参数</summary>
    [JsonPropertyName("temperature")] public bool? Temperature { get; set; }

    [JsonPropertyName("tool_call")] public bool? ToolCall { get; set; }

    [JsonPropertyName("reasoning")] public bool? Reasoning { get; set; }

    [JsonPropertyName("reasoning_options")] public List<ModelsDevRawReasoningOption>? ReasoningOptions { get; set; }

    [JsonPropertyName("canonical_model_id")] public string? CanonicalModelId { get; set; }
}

/// <summary>models.dev 的价格（USD 每百万 token）</summary>
internal sealed class ModelsDevRawCost
{
    [JsonPropertyName("input")] public decimal? Input { get; set; }

    [JsonPropertyName("output")] public decimal? Output { get; set; }

    [JsonPropertyName("cache_read")] public decimal? CacheRead { get; set; }

    [JsonPropertyName("cache_write")] public decimal? CacheWrite { get; set; }

    [JsonPropertyName("tiers")] public List<ModelsDevRawTier>? Tiers { get; set; }

    /// <summary>老字段：只有「超过 20 万」这一档，没有显式起点</summary>
    [JsonPropertyName("context_over_200k")] public ModelsDevRawOver200K? ContextOver200K { get; set; }
}

/// <summary>阶梯价的一档</summary>
internal sealed class ModelsDevRawTier
{
    [JsonPropertyName("tier")] public ModelsDevRawTierRange? Range { get; set; }

    [JsonPropertyName("input")] public decimal? Input { get; set; }

    [JsonPropertyName("output")] public decimal? Output { get; set; }

    [JsonPropertyName("cache_read")] public decimal? CacheRead { get; set; }

    [JsonPropertyName("cache_write")] public decimal? CacheWrite { get; set; }
}

/// <summary>阶梯起点</summary>
internal sealed class ModelsDevRawTierRange
{
    [JsonPropertyName("size")] public long? Size { get; set; }
}

/// <summary>超过 20 万上下文后的价（老字段）</summary>
internal sealed class ModelsDevRawOver200K
{
    [JsonPropertyName("input")] public decimal? Input { get; set; }

    [JsonPropertyName("output")] public decimal? Output { get; set; }

    [JsonPropertyName("cache_read")] public decimal? CacheRead { get; set; }

    [JsonPropertyName("cache_write")] public decimal? CacheWrite { get; set; }
}

/// <summary>窗口大小</summary>
internal sealed class ModelsDevRawLimit
{
    [JsonPropertyName("context")] public int? Context { get; set; }

    [JsonPropertyName("output")] public int? Output { get; set; }
}

/// <summary>输入模态（只看有没有 image）</summary>
internal sealed class ModelsDevRawModalities
{
    [JsonPropertyName("input")] public List<string>? Input { get; set; }
}

/// <summary>思考控制的一项声明（<c>effort</c> 带取值表，<c>toggle</c>/<c>budget</c> 不带）</summary>
internal sealed class ModelsDevRawReasoningOption
{
    [JsonPropertyName("type")] public string? Type { get; set; }

    [JsonPropertyName("values")] public List<string>? Values { get; set; }
}
