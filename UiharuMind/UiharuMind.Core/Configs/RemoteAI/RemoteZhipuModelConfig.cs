using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs.RemoteAI;

[SettingConfigDesc("ChatGLM")]
[SettingConfigDesc("智谱清言", LanguageUtils.ChineseSimplified)]
public class RemoteZhipuModelConfig : BaseRemoteModelConfig, IRemoteModelConfig
{
    // public override string? ConfigType { get; set; } = typeof(RemoteZhipuModelConfig).FullName;

    [SettingConfigDesc("custom name for the model, only used for display, no actual effect")]
    [SettingConfigDesc("自定义名字，仅用于显示，无实际作用", LanguageUtils.ChineseSimplified)]
    public override string ModelName { get; set; } = "ChatGLM-Flash";

    [SettingConfigDesc("path to the model, should start with http or https")]
    [SettingConfigDesc("模型远程地址，应以http或https开头", LanguageUtils.ChineseSimplified)]
    public override string ModelPath { get; set; } = "https://open.bigmodel.cn/api/paas/v4/chat/completions";

    [SettingConfigDesc("description of the model")]
    [SettingConfigDesc("模型描述", LanguageUtils.ChineseSimplified)]
    public override string ModelDescription { get; set; }

    [SettingConfigDesc("ID of the model, used to distinguish different models for calling")]
    [SettingConfigDesc("模型ID，用于区分调用的不同模型。\n请注意：目前除了 glm-4-flash 之外的模型都要收费。", LanguageUtils.ChineseSimplified)]
    [SettingConfigOptions([
        "glm-4-flash", "glm-4", "glm-4-plus", "glm-4-air", "glm-4-airx", "glm-4-long", "glm-4-flashx"
    ])]
    public override string ModelId { get; set; } = "glm-4-flash";

    public override int Port { get; set; }
    public override string ApiKey { get; set; }
}