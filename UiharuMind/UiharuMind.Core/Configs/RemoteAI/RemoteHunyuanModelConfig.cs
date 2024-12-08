using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Core.RemoteOpenAI;

namespace UiharuMind.Core.Configs.RemoteAI;

[SettingConfigDesc("Tencent Hunyuan")]
[SettingConfigDesc("腾讯混元(不支持角色对话)", LanguageUtils.ChineseSimplified)]
public class RemoteHunyuanModelConfig : BaseRemoteModelConfig, IRemoteModelConfig
{
    // public override string? ConfigType { get; set; } = typeof(RemoteHunyuanModelConfig).FullName;

    [SettingConfigDesc("custom name for the model, only used for display, no actual effect")]
    [SettingConfigDesc("自定义名字，仅用于显示，无实际作用", LanguageUtils.ChineseSimplified)]
    public override string ModelName { get; set; } = "Tencent Hunyuan-Lite";

    [SettingConfigDesc("path to the model, should start with http or https")]
    [SettingConfigDesc("模型远程地址，应以http或https开头", LanguageUtils.ChineseSimplified)]
    public override string ModelPath { get; set; } = "https://api.hunyuan.cloud.tencent.com/v1/chat/completions";

    [SettingConfigDesc("description of the model")]
    [SettingConfigDesc("模型描述", LanguageUtils.ChineseSimplified)]
    public override string ModelDescription { get; set; }

    [SettingConfigDesc("ID of the model, used to distinguish different models for calling")]
    [SettingConfigDesc("模型ID，用于区分调用的不同模型。\n请注意：目前除了 hunyuan-lite 之外的模型都要收费。", LanguageUtils.ChineseSimplified)]
    [SettingConfigOptions([
        "hunyuan-lite", "hunyuan-turbo", "hunyuan-pro", "hunyuan-standard", "hunyuan-standard-256K", "hunyuan-role",
        "hunyuan-functioncall", "hunyuan-code"
    ])]
    public override string ModelId { get; set; } = "hunyuan-lite";

    public override int Port { get; set; }
    public override string ApiKey { get; set; }
}