using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs.RemoteAI;

/// <summary>
/// OpenCode（opencode.ai 推理网关，OpenAI 兼容）。候选走运行时模型目录（ADR 0092）——
/// 它的 /v1/models 是 OpenRouter 式最简形状（仅 id），正好实测「缺字段默认展示」路径。
/// </summary>
[SettingConfigDesc("OpenCode")]
[SettingConfigDesc("OpenCode", LanguageUtils.ChineseSimplified)]
public class RemoteOpenCodeModelConfig : BaseRemoteModelConfig, IRemoteModelConfig
{
    public override string ModelName { get; set; } = "OpenCode";

    public override string ModelPath { get; set; } = "https://opencode.ai/inference/openai/v1/chat/completions";

    public override string WebsiteUrl { get; set; } = "https://opencode.ai";

    public override string ModelCatalogUrl { get; set; } = "https://opencode.ai/inference/v1/models";

    public override string ModelDescription { get; set; } = "";

    /// <summary>无默认模型：目录拉下来让用户挑</summary>
    public override string ModelId { get; set; } = "";

    public override int Port { get; set; }

    /// <summary>
    /// 网关下挂的模型各家不同，暂时沿用改动前的写法（<c>thinking.type</c> + <c>reasoning_effort</c>）。
    /// 是否被接受未验证，换格式前得先录一次真实请求。
    /// </summary>
    public override EThinkingFormat ThinkingFormat => EThinkingFormat.ThinkingType;
}
