using System.Text.Json.Nodes;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs.RemoteAI;

[SettingConfigDesc("ChatGLM")]
[SettingConfigDesc("智谱清言", LanguageUtils.ChineseSimplified)]
public class RemoteZhipuModelConfig : BaseRemoteModelConfig, IRemoteModelConfig
{
    // public override string? ConfigType { get; set; } = typeof(RemoteZhipuModelConfig).FullName;

    public override string ModelName { get; set; } = "ChatGLM-Flash";

    public override string ModelPath { get; set; } = "https://open.bigmodel.cn/api/paas/v4/chat/completions";

    public override string ModelDescription { get; set; } = "";

    public override string ModelId { get; set; } = "glm-4-flash";

    public override IReadOnlyDictionary<string, RemoteModelIdVariant> ModelIdVariants { get; } =
        new Dictionary<string, RemoteModelIdVariant>
        {
            ["glm-4-flash"] = new(ContextLength: 128000),
            ["glm-4.7-flash"] = new(ContextLength: 128000),
            ["glm-4"] = new(ContextLength: 128000),
            ["glm-4-plus"] = new(ContextLength: 128000),
            ["glm-4-air"] = new(ContextLength: 128000),
            ["glm-4-airx"] = new(ContextLength: 128000),
            ["glm-4-long"] = new(ContextLength: 1048576),
            ["glm-4-flashx"] = new(ContextLength: 128000),
            ["glm-4v-flash"] = new(ContextLength: 128000, IsVision: true),
            ["glm-4.6v-flash"] = new(ContextLength: 128000, IsVision: true),
            ["glm-4v-plus"] = new(ContextLength: 128000, IsVision: true),
            ["glm-4v"] = new(ContextLength: 128000, IsVision: true),
        };

    public override int Port { get; set; }
}