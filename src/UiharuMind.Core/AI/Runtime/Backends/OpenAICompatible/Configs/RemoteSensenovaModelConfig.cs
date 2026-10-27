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
            ["deepseek-flash"] = new(ContextLength: 1048576, MaxTokens: 393216, IsVision: true, RequiresReasoningContentRoundtrip: true),
            ["glm-5.2"] = new(ContextLength: 1048576, MaxTokens: 128000),
            ["kimi-k3"] = new(ContextLength: 1048576, MaxTokens: 131072, IsVision: true, OmitSamplingParams: true,
                RequiresReasoningContentRoundtrip: true),
            // 商汤自研(平台最新为 6.8)。官方文档参数推荐:多轮对话只回传 content、不回传 reasoning
            // (思考字段名是 reasoning 非 reasoning_content);与同平台 DeepSeek(携带 tools 时必须回传
            // reasoning_content)相反,这里显式关 roundtrip——roundtrip 三态化后默认 null=自动回传,
            // 不显式写 false 会被悄悄反转(评审发现的反转点)
            ["sensenova-6.8-flash-lite"] = new(ContextLength: 262144, MaxTokens: 65535, IsVision: true,
                RequiresReasoningContentRoundtrip: false, ThinkingField: "reasoning"),
        };

    public override int Port { get; set; }
}
