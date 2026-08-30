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
    public virtual string ModelName { get; set; }
    public virtual string ModelPath { get; set; }
    public virtual string ModelDescription { get; set; }
    public virtual string ModelId { get; set; }
    public virtual bool IsVision { get; set; }

    /// <summary>
    /// 模型最大上下文窗口(token 数),0 表示未设置
    /// </summary>
    [SettingConfigIgnoreDisplay]
    public virtual int ContextLength { get; set; }

    /// <summary>
    /// 各 ModelId 的预设能力(默认上下文、是否支持视觉),键为 ModelId。
    /// 供创建/编辑窗口在选择下拉框时预填默认值并展示视觉标记,不参与序列化。
    /// </summary>
    [JsonIgnore]
    public virtual IReadOnlyDictionary<string, RemoteModelIdVariant> ModelIdVariants => RemoteModelIdVariant.Empty;

    /// <summary>
    /// 思考力度
    /// </summary>
    /// <param name="thinkingMode">本次请求的思考力度</param>
    /// <returns>要写入请求 JSON 的键值对;Default 不干预返回空</returns>
    public virtual IReadOnlyList<KeyValuePair<string, JsonNode?>>? GetExtraParams(EThinkingMode thinkingMode)
    {
        return thinkingMode switch
        {
            EThinkingMode.None =>
            [
                new("thinking", new JsonObject { ["type"] = "disabled" }),
            ],
            EThinkingMode.Medium =>
            [
                new("thinking", new JsonObject { ["type"] = "enabled" }),
                new("reasoning_effort", JsonValue.Create("medium")),
            ],
            EThinkingMode.High =>
            [
                new("thinking", new JsonObject { ["type"] = "enabled" }),
                new("reasoning_effort", JsonValue.Create("high")),
            ],
            EThinkingMode.Max =>
            [
                new("thinking", new JsonObject { ["type"] = "enabled" }),
                new("reasoning_effort", JsonValue.Create("max")),
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
    /// 思考模式下,助手消息带 tool_calls 时是否要求原样带回当时的 reasoning_content。
    /// 用户显式覆盖优先,否则按当前 ModelId 查预设表——这是模型本身的接口约束,
    /// 同一供应商转售的其它模型未必需要,不能挂在配置类上固定死。
    /// </summary>
    public virtual bool RequiresReasoningContentRoundtrip =>
        RequiresReasoningContentRoundtripOverride ??
        (ModelIdVariants.TryGetValue(ModelId, out RemoteModelIdVariant? variant) &&
         variant.RequiresReasoningContentRoundtrip);

    [SettingConfigIgnoreDisplay] public virtual int Port { get; set; }

    public BaseRemoteModelConfig()
    {
        ConfigType = GetType().Name;
    }
}