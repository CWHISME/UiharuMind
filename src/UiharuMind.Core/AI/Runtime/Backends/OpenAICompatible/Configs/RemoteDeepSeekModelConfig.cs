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
            // deepseek-flash 即 DeepSeek-V4.1-Flash;deepseek-v4-flash 为已退役旧名,官方仍兼容且同样由 V4.1-Flash 服务。
            // 「思考模式下带 tool_calls 的助手消息必须含 reasoning_content」由基类按模型 ID 含 deepseek 自动判定;
            // roundtrip 默认 auto(有正文就回传),这里只留能力字段。
            ["deepseek-flash"] = new(ContextLength: 1048576, MaxTokens: 393216, IsVision: true),
            ["deepseek-v4-flash"] = new(ContextLength: 1048576, MaxTokens: 393216, IsVision: true),
            ["deepseek-v4-pro"] = new(ContextLength: 1048576, MaxTokens: 393216),
        };

    public override int Port { get; set; }
}
