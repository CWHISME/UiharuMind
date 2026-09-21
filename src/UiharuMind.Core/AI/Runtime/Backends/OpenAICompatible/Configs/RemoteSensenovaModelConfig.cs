using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs.RemoteAI;

[SettingConfigDesc("Sensenova")]
[SettingConfigDesc("商汤科技(Sensenova)", LanguageUtils.ChineseSimplified)]
public class RemoteSensenovaModelConfig : BaseRemoteModelConfig, IRemoteModelConfig
{
    public override string ModelName { get; set; } = "Deepseek-V4-Flash";

    public override string ModelPath { get; set; } = "https://token.sensenova.cn/v1/chat/completions";

    public override string WebsiteUrl { get; set; } = "https://platform.sensenova.cn";

    public override string ModelDescription { get; set; } = "";

    public override string ModelId { get; set; } = "deepseek-v4-flash";

    public override IReadOnlyDictionary<string, RemoteModelIdVariant> ModelIdVariants { get; } =
        new Dictionary<string, RemoteModelIdVariant>
        {
            // 商汤平台转售的第三方模型,ID 与参数沿用平台口径
            ["deepseek-v4-flash"] = new(ContextLength: 1048576, MaxTokens: 327680, RequiresReasoningContentRoundtrip: true),
            ["deepseek-v4-pro"] = new(ContextLength: 1048576, MaxTokens: 327680, RequiresReasoningContentRoundtrip: true),
            ["glm-5.2"] = new(ContextLength: 1048576, MaxTokens: 128000),
            ["kimi-k3"] = new(ContextLength: 1048576, MaxTokens: 131072,IsVision:true, OmitSamplingParams: true,
                RequiresReasoningContentRoundtrip: true),
            // 商汤自研(平台最新为 6.8)
            ["sensenova-6.8-flash-lite"] = new(ContextLength: 262144, MaxTokens: 65535, IsVision: true),
        };

    public override int Port { get; set; }
}
