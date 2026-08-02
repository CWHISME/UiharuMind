using System.Text.Json.Nodes;
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

    [SettingConfigDesc("Is Thinking")]
    [SettingConfigDesc("是否是思考模型", LanguageUtils.ChineseSimplified)]
    public virtual bool IsThinking { get; set; }

    /// <summary>
    /// 思考力度 → 请求参数的通用翻译:同时给出智谱系 thinking 与 OpenAI 系 reasoning_effort,
    /// 不认识对应字段的服务端会忽略;有更严格要求的后端自行覆写。
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
            _ => null,
        };
    }

    [SettingConfigIgnoreDisplay] public virtual int Port { get; set; }
    [SettingConfigIgnoreDisplay] public virtual string ApiKey { get; set; }

    public BaseRemoteModelConfig()
    {
        ConfigType = GetType().Name;
    }
}