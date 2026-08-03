using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs.RemoteAI;

[SettingConfigDesc("ChatGLM(Vision)")]
[SettingConfigDesc("智谱清言(Vision)", LanguageUtils.ChineseSimplified)]
public class RemoteZhipuVisionModelConfig : BaseRemoteModelConfig, IRemoteModelConfig
{
    // public override string? ConfigType { get; set; } = typeof(RemoteZhipuModelConfig).FullName;

    public override string ModelName { get; set; } = "GLM-4V-Flash";

    public override string ModelPath { get; set; } = "https://open.bigmodel.cn/api/paas/v4/chat/completions";

    public override string ModelDescription { get; set; } = "";

    [SettingConfigOptions([
        "glm-4v-flash", "glm-4.6v-flash", "glm-4v-plus", "glm-4v"
    ])]
    public override string ModelId { get; set; } = "glm-4v-flash";

    public override int Port { get; set; }

    public override bool IsVision
    {
        get => true;
        set { }
    }
}