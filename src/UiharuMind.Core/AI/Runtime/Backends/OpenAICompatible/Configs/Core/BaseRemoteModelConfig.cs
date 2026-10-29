using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Configs;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Core.RemoteOpenAI.ModelCatalog;

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

    /// <summary>
    /// 用户显式指定的"是否视觉模型"。<c>null</c> 表示跟随推导（<see cref="ModelCapabilityResolver.ResolveVision"/>）。
    ///
    /// 为什么不直接存 bool：能力本来就是"表里查出来的"，存下来就冻结成建模型那一刻的判断。
    /// 转售商目录缺行时预填值可能是错的（实测 commandcode 下的 MiMo 就是这样被存成非视觉，
    /// 结果图片发不出去），而表会在下一次生成时修好——只有留出"未覆盖"这一档，修好的值才带得过去。
    /// 同款结构见 <see cref="ReplayExistingReasoningOverride"/>。
    /// </summary>
    [SettingConfigIgnoreDisplay]
    public virtual bool? IsVisionOverride { get; set; }

    /// <summary>
    /// 是否视觉模型：用户显式设置 &gt; 预设变体 &gt; 供应商条目 &gt; canonical 兜底。
    /// 写入即视为用户显式指定（见 <see cref="IsVisionOverride"/>）
    /// </summary>
    [JsonIgnore]
    [SettingConfigIgnoreDisplay]
    public virtual bool IsVision
    {
        get => IsVisionOverride ?? ModelCapabilityResolver.ResolveVision(this);
        set => IsVisionOverride = value;
    }

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
    /// 模型思考档位的 wire 值（如 <c>low</c> / <c>medium</c> / <c>xhigh</c>）。
    /// 空串表示不干预、<see cref="ThinkingEfforts.Off"/> 表示关闭，其余由 <see cref="GetExtraParams"/> 原样下发。
    ///
    /// 存字符串而不是枚举：能接受哪些值由模型自己声明（<see cref="SupportedThinkingEfforts"/>），
    /// 固定枚举会让只认三档的模型收到 <c>max</c> 直接 400。JSON 键沿用旧名 <c>ThinkingMode</c>，
    /// 旧存档里的枚举名由 <see cref="ThinkingEfforts.Normalize"/> 翻译，不需要迁移脚本。
    /// </summary>
    [SettingConfigIgnoreDisplay]
    [JsonPropertyName("ThinkingMode")]
    public virtual string ThinkingEffort { get; set; } = ThinkingEfforts.Auto;

    /// <summary>
    /// 当前模型声明支持的思考档位（不含「不干预」与「关闭」两项，这两项永远可选）。
    /// 查不到声明时回退到 <see cref="ThinkingEfforts.Fallback"/>。
    /// </summary>
    [JsonIgnore]
    [SettingConfigIgnoreDisplay]
    public virtual IReadOnlyList<string> SupportedThinkingEfforts =>
        ModelCapabilityResolver.ResolveEffortOptions(this);

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
    /// <returns>要写入请求 JSON 的键值对;档位为「不干预」时仅给足预算</returns>
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

        // max_tokens 必须与思考档位同步给:服务端默认 8192 被思考吃光后正文为 0,
        // 表现为「思考完就没有正式对话」。这是用户最常用的「不干预」档,漏了等于没修
        string effort = ThinkingEfforts.Normalize(ThinkingEffort);
        if (effort.Length == 0) return [new("max_tokens", JsonValue.Create(maxTokens))];

        // 关闭档不思考,给 8192 也够正文用;其余档位原样下发声明值——
        // 不再做 low/medium/high/max 的四档映射,模型声明什么就发什么
        if (ThinkingEfforts.IsOff(effort))
        {
            return
            [
                new("thinking", new JsonObject { ["type"] = "disabled" }),
                new("max_tokens", JsonValue.Create(maxTokens)),
            ];
        }

        return
        [
            new("thinking", new JsonObject { ["type"] = "enabled" }),
            new("reasoning_effort", JsonValue.Create(effort)),
            new("max_tokens", JsonValue.Create(maxTokens)),
        ];
    }

    /// <summary>
    /// 用户在编辑窗口里手动覆盖的开关;未设置(<c>null</c>)时跟随 <see cref="ModelIdVariants"/>
    /// 按当前 <see cref="ModelId"/> 给出的预设判断。留出"未覆盖"这一档是为了让已经建好的配置存档
    /// (不带这个字段,反序列化后天然是 null)能在不重建的前提下吃到预设表的判断结果——
    /// 同款结构可参照 <see cref="ContextLength"/> 用 0 表示未设置、消费时才按预设表兜底。
    /// </summary>
    public virtual bool? ReplayExistingReasoningOverride { get; set; }

    /// <summary>
    /// 历史消息里<b>已有</b>思考正文时，是否原样回传（有正文就发，见
    /// <see cref="RequireReasoningOnEveryAssistantMessage"/> 管「没正文时字段能不能缺」）。
    /// 用户显式覆盖优先,否则按当前 ModelId 查预设表——这是模型本身的接口约束,
    /// 同一供应商转售的其它模型未必需要,不能挂在配置类上固定死。
    /// <c>null</c>=自动(历史有思考就回传),<c>true</c>/<c>false</c>=显式要求/禁止。
    /// </summary>
    public virtual bool? ReplayExistingReasoning =>
        ReplayExistingReasoningOverride ??
        (ModelIdVariants.TryGetValue(ModelId, out RemoteModelIdVariant? variant)
            ? variant.ReplayExistingReasoning
            : null);

    /// <summary>
    /// 运行时被告知「每一条助手消息都要带思考」的 ModelId（按 ModelId 分开记，与
    /// <see cref="ObservedThinkingFields"/> 同构）。由 <c>ReasoningRequirementErrorHandler</c>
    /// 在服务端报 <c>Missing required parameter: 'messages[n].reasoning_content'</c> 时写入并重发一次。
    ///
    /// 为什么靠学而不是靠表：这条约束是<b>按模型</b>的，同一网关下也不一致——
    /// 实测 CommandCode 的 <c>deepseek/deepseek-v4.1-flash</c> 只要求带 tool_calls 的那些（88 次请求、
    /// 105 条无思考的纯文本回复全部通过），而 <c>reflection/beam:free</c> 连纯文本回复都要求。
    /// 目录里几十个模型补不出表，等服务端亲口说一次比猜准。
    /// </summary>
    [SettingConfigIgnoreDisplay]
    public HashSet<string> ReasoningEveryAssistantModelIds { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 每一条助手消息都必须带思考字段:有正文回正文,没正文补空串。
    ///
    /// 来源三条：预设表显式声明、运行时从服务端 400 里学到（<see cref="ReasoningEveryAssistantModelIds"/>）、
    /// 或模型 ID 含 <c>deepseek</c> 的兜底——转售的 DeepSeek（CommandCode/OpenCode 等目录供应商）
    /// 不在预设表里，但口径与官方一致（opencode 客户端也是这么默认的：
    /// <c>requireReasoning ?? (reasoningField 已配 || provider/endpoint/modelID 是 deepseek)</c>）。
    ///
    /// 与思考档位无关:校验发生在服务端解析历史那一层,与本次是否思考无关。
    /// </summary>
    public bool RequireReasoningOnEveryAssistantMessage =>
        (ModelIdVariants.TryGetValue(ModelId, out RemoteModelIdVariant? variant) &&
         variant.RequireReasoningOnEveryAssistantMessage) ||
        ReasoningEveryAssistantModelIds.Contains(ModelId) ||
        ModelId.Contains("deepseek", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 记下「当前 ModelId 要求每条助手消息都带思考」。落盘由调用方负责（同 <c>TryFixMaxTokens</c>）。
    /// </summary>
    /// <returns>是否是新记下的（已记过时返回 false，重发没有意义）</returns>
    public bool LearnRequireReasoningOnEveryAssistantMessage() =>
        !string.IsNullOrWhiteSpace(ModelId) && ReasoningEveryAssistantModelIds.Add(ModelId);

    /// <summary>
    /// 运行时观测到的思考 wire 字段名（reasoning_content/reasoning/reasoning_text），**按 ModelId 分开记**。
    /// 同一端点的不同模型字段名可以不同（商汤转售的 DeepSeek 用 reasoning_content、自研 6.8 用 reasoning），
    /// 记成一个标量就会在切 ModelId 时互相污染，所以键是观测时的 ModelId。空表表示尚未观测。
    ///
    /// 只描述<b>响应</b>：请求侧的键名见 <see cref="RequestThinkingField"/>，两者不是一回事
    /// （CommandCode 的响应用 reasoning、请求只认 reasoning_content）。
    /// </summary>
    [SettingConfigIgnoreDisplay]
    public Dictionary<string, string> ObservedThinkingFields { get; set; } = new();

    /// <summary>
    /// 回填思考正文用的<b>请求侧</b> wire 键名：该 ModelId 的预设表 → <c>reasoning_content</c>。
    ///
    /// 不取运行时观测值：观测到的是<b>响应</b>里的字段名，请求侧未必同名——
    /// CommandCode 的响应给 <c>reasoning</c>，请求体里却只认 <c>reasoning_content</c>，
    /// 拿观测值回填等于发一个服务端不认的键，被当成「缺字段」直接 400。
    /// 请求侧确实不用默认名的服务商，在预设表里显式声明（如商汤自研 6.8 的 <c>reasoning</c>）。
    /// </summary>
    public string RequestThinkingField =>
        ModelIdVariants.TryGetValue(ModelId, out RemoteModelIdVariant? variant) &&
        !string.IsNullOrWhiteSpace(variant.ThinkingField)
            ? variant.ThinkingField
            : "reasoning_content";

    /// <summary>
    /// 接口要的是「回填键名」即 <see cref="RequestThinkingField"/>。显式实现，
    /// 避免有人把配置当模型用时读到存储侧的 <see cref="ObservedThinkingFields"/> 而拿不到兜底值。
    /// </summary>
    string ILlmModel.ThinkingField => RequestThinkingField;

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