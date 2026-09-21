using System.Text.Json.Nodes;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs.RemoteAI;

[SettingConfigDesc("ChatGLM")]
[SettingConfigDesc("智谱清言", LanguageUtils.ChineseSimplified)]
public class RemoteZhipuModelConfig : BaseRemoteModelConfig, IRemoteModelConfig
{
    // public override string? ConfigType { get; set; } = typeof(RemoteZhipuModelConfig).FullName;

    public override string ModelName { get; set; } = "GLM-4.7-Flash";

    public override string ModelPath { get; set; } = "https://open.bigmodel.cn/api/paas/v4/chat/completions";

    public override string WebsiteUrl { get; set; } = "https://open.bigmodel.cn";

    public override string ModelDescription { get; set; } = "";

    public override string ModelId { get; set; } = "glm-4.7-flash";

    public override IReadOnlyDictionary<string, RemoteModelIdVariant> ModelIdVariants { get; } =
        new Dictionary<string, RemoteModelIdVariant>
        {
            // 文本模型(参数以 docs.bigmodel.cn 模型概览为准)
            ["glm-5.3"] = new(ContextLength: 1048576, MaxTokens: 131072),
            ["glm-5.2"] = new(ContextLength: 1048576, MaxTokens: 131072),
            ["glm-5.1"] = new(ContextLength: 204800, MaxTokens: 131072),
            ["glm-5"] = new(ContextLength: 204800, MaxTokens: 131072),
            ["glm-5-turbo"] = new(ContextLength: 204800, MaxTokens: 131072),
            ["glm-4.7"] = new(ContextLength: 204800, MaxTokens: 131072),
            ["glm-4.7-flashx"] = new(ContextLength: 204800, MaxTokens: 131072),
            ["glm-4.7-flash"] = new(ContextLength: 204800, MaxTokens: 131072, Alias: "glm-4.7-flash (free)"),
            ["glm-4.6"] = new(ContextLength: 204800, MaxTokens: 131072),
            ["glm-4.5-air"] = new(ContextLength: 131072, MaxTokens: 98304),
            ["glm-4.5-airx"] = new(ContextLength: 131072, MaxTokens: 98304),
            ["glm-4.5-flash"] = new(ContextLength: 131072, MaxTokens: 98304, Alias: "glm-4.5-flash (free)"),
            ["glm-4-long"] = new(ContextLength: 1048576, MaxTokens: 4096),
            ["glm-4-flashx-250414"] = new(ContextLength: 131072, MaxTokens: 16384),
            ["glm-4-flash-250414"] = new(ContextLength: 131072, MaxTokens: 16384, Alias: "glm-4-flash-250414 (free)"),
            // 视觉理解模型
            ["glm-5v-turbo"] = new(ContextLength: 204800, MaxTokens: 131072, IsVision: true),
            ["glm-4.6v"] = new(ContextLength: 131072, MaxTokens: 32768, IsVision: true),
            ["glm-4.6v-flash"] = new(ContextLength: 131072, MaxTokens: 32768, IsVision: true, Alias: "glm-4.6v-flash (free)"),
            ["glm-4.1v-thinking-flashx"] = new(ContextLength: 65536, MaxTokens: 16384, IsVision: true),
            ["glm-4.1v-thinking-flash"] = new(ContextLength: 65536, MaxTokens: 16384, IsVision: true, Alias: "glm-4.1v-thinking-flash (free)"),
            ["glm-4v-flash"] = new(ContextLength: 16384, MaxTokens: 1024, IsVision: true, Alias: "glm-4v-flash (free)"),
            // 已从官方概览下线但仍可调用的旧模型(实测响应更快)
            ["glm-4"] = new(ContextLength: 128000, MaxTokens: 8192),
        };

    public override int Port { get; set; }
}
