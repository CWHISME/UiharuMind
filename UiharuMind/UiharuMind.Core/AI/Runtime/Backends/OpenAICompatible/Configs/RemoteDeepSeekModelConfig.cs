using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs.RemoteAI;

[SettingConfigDesc("DeepSeek")]
[SettingConfigDesc("DeepSeek", LanguageUtils.ChineseSimplified)]
public class RemoteDeepSeekModelConfig : BaseRemoteModelConfig, IRemoteModelConfig
{
    public override string ModelName { get; set; } = "DeepSeek-Flash";

    public override string ModelPath { get; set; } = "https://api.deepseek.com/chat/completions";

    public override string WebsiteUrl { get; set; } = "https://platform.deepseek.com";

    public override string ModelDescription { get; set; } = "";

    public override string ModelId { get; set; } = "deepseek-flash";

    public override IReadOnlyDictionary<string, RemoteModelIdVariant> ModelIdVariants { get; } =
        new Dictionary<string, RemoteModelIdVariant>
        {
            // deepseek-flash 即 DeepSeek-V4.1-Flash;deepseek-v4-flash 为已退役旧名,官方仍兼容且同样由 V4.1-Flash 服务
            ["deepseek-flash"] = new(ContextLength: 1048576, MaxTokens: 393216, IsVision: true, RequiresReasoningContentRoundtrip: true),
            ["deepseek-v4-flash"] = new(ContextLength: 1048576, MaxTokens: 393216, IsVision: true, RequiresReasoningContentRoundtrip: true),
            ["deepseek-v4-pro"] = new(ContextLength: 1048576, MaxTokens: 393216, RequiresReasoningContentRoundtrip: true),
        };

    public override int Port { get; set; }
}
