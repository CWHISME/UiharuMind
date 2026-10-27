using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Configs;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs.RemoteAI;

public class BaseRemoteModelConfig : ConfigBase, ILlmModel
{
    [SettingConfigIgnoreDisplay] public string? ConfigType { get; set; }
    public virtual string ModelName { get; set; } = string.Empty;
    public virtual string ModelPath { get; set; } = string.Empty;

    /// <summary>
    /// 服务商官网/开放平台地址,用于界面一键跳转;空表示无固定官网(如自定义配置)
    /// </summary>
    [JsonIgnore]
    public virtual string WebsiteUrl { get; set; } = string.Empty;

    /// <summary>
    /// 服务商的模型目录地址（/v1/models），空表示无目录。
    /// 预设写常量；自定义配置在表单填（不随模型存档，ADR 0092）。
    /// </summary>
    [JsonIgnore]
    public virtual string ModelCatalogUrl { get; set; } = string.Empty;

    public virtual string ModelDescription { get; set; } = string.Empty;
    public virtual string ModelId { get; set; } = string.Empty;
    public virtual bool IsVision { get; set; }

    /// <summary>
    /// 是否不发送采样参数(temperature/top_p/presence_penalty/frequency_penalty)。
    /// 采样参数固定的模型(如 Kimi)会拒绝显式传值的请求。新建模型时按预设表预填一次,之后各存各的。
    /// </summary>
    public virtual bool OmitSamplingParams { get; set; }

    /// <summary>
    /// 模型最大上下文窗口(token 数),0 表示未设置
    /// </summary>
    [SettingConfigIgnoreDisplay]
    public virtual int ContextLength { get; set; }

    /// <summary>
    /// 模型最大输出预算(max_tokens)。思考与正文共用一个输出配额:
    /// 服务端默认预算(实测 deepseek-v4-flash 是 8192)会被超长思考整个吃光,
    /// 正文一个字都发不出,finish_reason=length 直接收尾——这正是"思考一长就断"的根因。
    /// 0 表示未设置,发送时取默认预算(<see cref="DefaultMaxTokens"/>)。
    /// </summary>
    [SettingConfigIgnoreDisplay]
    public virtual int MaxTokens { get; set; }

    /// <summary>
    /// 模型思考力度。Default 表示不干预,沿用模型自身行为;
    /// 其余档位由 <see cref="GetExtraParams"/> 翻译为请求参数。
    /// </summary>
    [SettingConfigIgnoreDisplay]
    public virtual EThinkingMode ThinkingMode { get; set; } = EThinkingMode.Default;

    /// <summary>
    /// 各 ModelId 的预设能力(默认上下文、是否支持视觉),键为 ModelId。
    /// 供创建/编辑窗口在选择下拉框时预填默认值并展示视觉标记,不参与序列化。
    /// </summary>
    [JsonIgnore]
    public virtual IReadOnlyDictionary<string, RemoteModelIdVariant> ModelIdVariants => RemoteModelIdVariant.Empty;

    /// <summary>
    /// 既没显式设置、预设表也没声明时的输出预算兜底。思考与正文共用一个配额，
    /// 给小了会被思考吃光、正文一个字发不出，因此取一个足够大的值。
    /// </summary>
    public const int DefaultMaxTokens = 65535;

    /// <summary>
    /// 思考力度
    /// </summary>
    /// <returns>要写入请求 JSON 的键值对;模型配置为 Default 仅给足预算,不干预思考参数</returns>
    public virtual IReadOnlyList<KeyValuePair<string, JsonNode?>>? GetExtraParams()
    {
        // 输出预算优先级:模型配置里显式填的 MaxTokens → 当前 ModelId 的预设默认值
        // (像 ContextLength 一样按模型声明) → 思考档位的兜底值。
        // 显式值优先,否则手动设过 max_tokens 的模型一选思考档就被覆盖掉
        int maxTokens = MaxTokens > 0
            ? MaxTokens
            : (ModelIdVariants.TryGetValue(ModelId, out RemoteModelIdVariant? preset) && preset.MaxTokens > 0
                ? preset.MaxTokens
                : DefaultMaxTokens);

        return ThinkingMode switch
        {
            // max_tokens 必须与思考档位同步给:服务端默认 8192 被思考吃光后正文为 0,
            // 表现为「思考完就没有正式对话」。None 档不思考,给 8192 也够正文用
            EThinkingMode.None =>
            [
                new("thinking", new JsonObject { ["type"] = "disabled" }),
                new("max_tokens", JsonValue.Create(maxTokens)),
            ],
            // 跟随模型配置(不干预 thinking/reasoning_effort),但预算必须给够——
            // 服务端默认 8192 被思考吃光后正文为 0,表现为「思考完就没有正式对话」。
            // 这是用户最常用的档位,漏了等于没修。MaxTokens 显式设置时优先用它的值
            EThinkingMode.Default =>
            [
                new("max_tokens", JsonValue.Create(maxTokens)),
            ],
            EThinkingMode.Light =>
            [
                new("thinking", new JsonObject { ["type"] = "enabled" }),
                new("reasoning_effort", JsonValue.Create("low")),
                new("max_tokens", JsonValue.Create(maxTokens)),
            ],
            EThinkingMode.Medium =>
            [
                new("thinking", new JsonObject { ["type"] = "enabled" }),
                new("reasoning_effort", JsonValue.Create("medium")),
                new("max_tokens", JsonValue.Create(maxTokens)),
            ],
            EThinkingMode.High =>
            [
                new("thinking", new JsonObject { ["type"] = "enabled" }),
                new("reasoning_effort", JsonValue.Create("high")),
                new("max_tokens", JsonValue.Create(maxTokens)),
            ],
            EThinkingMode.Max =>
            [
                new("thinking", new JsonObject { ["type"] = "enabled" }),
                new("reasoning_effort", JsonValue.Create("max")),
                new("max_tokens", JsonValue.Create(maxTokens)),
            ],
            _ => null,
        };
    }

    /// <summary>
    /// 用户在编辑窗口里手动覆盖的开关;未设置(<c>null</c>)时跟随 <see cref="ModelIdVariants"/>
    /// 按当前 <see cref="ModelId"/> 给出的预设判断。留出"未覆盖"这一档是为了让已经建好的配置存档
    /// (不带这个字段,反序列化后天然是 null)能在不重建的前提下吃到预设表的判断结果——
    /// 同款结构可参照 <see cref="ContextLength"/> 用 0 表示未设置、消费时才按预设表兜底。
    /// </summary>
    public virtual bool? RequiresReasoningContentRoundtripOverride { get; set; }

    /// <summary>
    /// 思考模式下,助手消息带 tool_calls 时是否要求原样带回当时的思考正文。
    /// 用户显式覆盖优先,否则按当前 ModelId 查预设表——这是模型本身的接口约束,
    /// 同一供应商转售的其它模型未必需要,不能挂在配置类上固定死。
    /// <c>null</c>=自动(历史有思考就回传),<c>true</c>/<c>false</c>=显式要求/禁止。
    /// </summary>
    public virtual bool? RequiresReasoningContentRoundtrip =>
        RequiresReasoningContentRoundtripOverride ??
        (ModelIdVariants.TryGetValue(ModelId, out RemoteModelIdVariant? variant)
            ? variant.RequiresReasoningContentRoundtrip
            : null);

    /// <summary>
    /// 思考模式下助手消息带 tool_calls 时,<b>没有思考正文也必须带思考字段</b>(空串)。
    ///
    /// 与 <see cref="RequiresReasoningContentRoundtrip"/> 是两件事:那个管"有正文要不要回传",
    /// 这个管"没正文时字段能不能缺"。DeepSeek 类服务端把后者当硬校验,缺字段直接 400——
    /// 典型触发点是历史里那条带 tool_calls 的消息本来就是关思考的档位跑出来的,没有正文可回传。
    ///
    /// 消费侧只在本次请求不是 <see cref="EThinkingMode.None"/> 时启用:None 档显式发 thinking:disabled,
    /// 服务端按正文调用处理,不要求该字段;Default 及各级思考档位都可能思考,保守补上。
    ///
    /// 这是模型侧的硬约束,只从预设表读:不落配置、不给手动覆盖(与 roundtrip 不同,用户没法靠观察判断)。
    /// </summary>
    public bool RequiresReasoningContentOnAssistantMessages =>
        ModelIdVariants.TryGetValue(ModelId, out RemoteModelIdVariant? variant) &&
        variant.RequiresReasoningContentOnAssistantMessages;

    /// <summary>
    /// 运行时观测到的思考 wire 字段名（reasoning_content/reasoning/reasoning_text），**按 ModelId 分开记**。
    /// 同一端点的不同模型字段名可以不同（商汤转售的 DeepSeek 用 reasoning_content、自研 6.8 用 reasoning），
    /// 记成一个标量就会在切 ModelId 时互相污染，所以键是观测时的 ModelId。空表表示尚未观测。
    /// </summary>
    [SettingConfigIgnoreDisplay]
    public Dictionary<string, string> ObservedThinkingFields { get; set; } = new();

    /// <summary>
    /// 回填思考正文用的 wire 键名：当前 ModelId 的观测值 → 该 ModelId 的预设表 → reasoning_content。
    /// 请求侧改写只认这个值，不写死键名。
    /// </summary>
    public string EffectiveThinkingField =>
        ObservedThinkingFields.TryGetValue(ModelId, out string? observed) && !string.IsNullOrWhiteSpace(observed)
            ? observed
            : ModelIdVariants.TryGetValue(ModelId, out RemoteModelIdVariant? variant) &&
              !string.IsNullOrWhiteSpace(variant.ThinkingField)
                ? variant.ThinkingField
                : "reasoning_content";

    /// <summary>
    /// 接口要的是「回填键名」即 <see cref="EffectiveThinkingField"/>。显式实现，
    /// 避免有人把配置当模型用时读到存储侧的 <see cref="ObservedThinkingFields"/> 而拿不到兜底值。
    /// </summary>
    string ILlmModel.ThinkingField => EffectiveThinkingField;

    [SettingConfigIgnoreDisplay] public virtual int Port { get; set; }

    /// <summary>
    /// 模型答话的接口格式（wire）：<see cref="EApiFormat.ChatCompletions"/> 走 OpenAI 兼容线，
    /// <see cref="EApiFormat.Messages"/> 走 Anthropic Messages（v1 未实现，二期接）。
    /// 目录解析时按 supported_endpoints 写入（ADR 0092）。
    /// </summary>
    [SettingConfigIgnoreDisplay]
    public virtual EApiFormat ApiFormat { get; set; } = EApiFormat.ChatCompletions;

    public BaseRemoteModelConfig()
    {
        ConfigType = GetType().Name;
    }
}