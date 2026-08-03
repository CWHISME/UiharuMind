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

    [SettingConfigOptions([
        "glm-4-flash", "glm-4.7-flash", "glm-4", "glm-4-plus", "glm-4-air", "glm-4-airx", "glm-4-long", "glm-4-flashx"
    ])]
    public override string ModelId { get; set; } = "glm-4-flash";

    public override int Port { get; set; }
}