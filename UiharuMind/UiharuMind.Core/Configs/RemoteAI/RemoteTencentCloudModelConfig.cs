using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs.RemoteAI;

[SettingConfigDesc("Tencent Cloud")]
[SettingConfigDesc("腾讯云知识引擎", LanguageUtils.ChineseSimplified)]
public class RemoteTencentCloudModelConfig : BaseRemoteModelConfig, IRemoteModelConfig
{
    [SettingConfigDesc("custom name for the model, only used for display, no actual effect")]
    [SettingConfigDesc("自定义名字，仅用于显示，无实际作用", LanguageUtils.ChineseSimplified)]
    public override string ModelName { get; set; } = "Tencent-Cloud";

    [SettingConfigDesc("path to the model, should start with http or https")]
    [SettingConfigDesc("模型远程地址，应以http或https开头", LanguageUtils.ChineseSimplified)]
    public override string ModelPath { get; set; } = "https://api.lkeap.cloud.tencent.com/v1";

    [SettingConfigDesc("description of the model")]
    [SettingConfigDesc("模型描述", LanguageUtils.ChineseSimplified)]
    public override string ModelDescription { get; set; } = "";

    [SettingConfigDesc("ID of the model, used to distinguish different models for calling")]
    [SettingConfigDesc("模型ID，用于区分不同模型。",
        LanguageUtils.ChineseSimplified)]
    [SettingConfigOptions(["deepseek-v3", "deepseek-r1"])]

    public override string ModelId { get; set; } = "deepseek-v3";

    public override int Port { get; set; }
    public override string ApiKey { get; set; } = "";
}