using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs.RemoteAI;

[SettingConfigDesc("DeepSeek")]
[SettingConfigDesc("DeepSeek", LanguageUtils.ChineseSimplified)]
public class RemoteDeepSeekModelConfig : BaseRemoteModelConfig, IRemoteModelConfig
{
    public override string ModelName { get; set; } = "Deepseek-Reasoner";

    public override string ModelPath { get; set; } = "https://api.deepseek.com/chat/completions";

    public override string ModelDescription { get; set; } = "";

    [SettingConfigOptions(["deepseek-v4-flash", "deepseek-v4-pro"])]
    public override string ModelId { get; set; } = "deepseek-v4-flash";

    public override int Port { get; set; }
}