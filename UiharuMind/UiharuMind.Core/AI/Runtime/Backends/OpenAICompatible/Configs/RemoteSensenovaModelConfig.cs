using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs.RemoteAI;

[SettingConfigDesc("Sensenova")]
[SettingConfigDesc("商汤科技(Sensenova)", LanguageUtils.ChineseSimplified)]
public class RemoteSensenovaModelConfig : BaseRemoteModelConfig, IRemoteModelConfig
{
    public override string ModelName { get; set; } = "Deepseek-V4-Flash";

    public override string ModelPath { get; set; } = "https://token.sensenova.cn/v1/chat/completions";

    public override string ModelDescription { get; set; } = "";

    public override string ModelId { get; set; } = "deepseek-v4-flash";

    public override IReadOnlyDictionary<string, RemoteModelIdVariant> ModelIdVariants { get; } =
        new Dictionary<string, RemoteModelIdVariant>
        {
            ["deepseek-v4-flash"] = new(ContextLength: 1048576),
            ["glm-5.2"] = new(ContextLength: 1048576),
            ["sensenova-6.8-flash-lite"] = new(ContextLength: 262144, true),
        };

    public override int Port { get; set; }
}