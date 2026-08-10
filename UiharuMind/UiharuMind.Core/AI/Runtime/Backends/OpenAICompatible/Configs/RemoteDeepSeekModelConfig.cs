using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs.RemoteAI;

[SettingConfigDesc("DeepSeek")]
[SettingConfigDesc("DeepSeek", LanguageUtils.ChineseSimplified)]
public class RemoteDeepSeekModelConfig : BaseRemoteModelConfig, IRemoteModelConfig
{
    public override string ModelName { get; set; } = "Deepseek-V4-Flash";

    public override string ModelPath { get; set; } = "https://api.deepseek.com/chat/completions";

    public override string ModelDescription { get; set; } = "";

    public override string ModelId { get; set; } = "deepseek-v4-flash";

    public override IReadOnlyDictionary<string, RemoteModelIdVariant> ModelIdVariants { get; } =
        new Dictionary<string, RemoteModelIdVariant>
        {
            ["deepseek-v4-flash"] = new(ContextLength: 1048576),
            ["deepseek-v4-pro"] = new(ContextLength: 1048576),
        };

    public override int Port { get; set; }
}