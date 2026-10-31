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
    /// 三条线不在同一段前缀上（实测文档 opencode.ai/v2/docs/console/api/inference）：
    /// chat 与 responses 在 <c>/inference/openai/v1/</c>，Anthropic 线在 <c>/inference/anthropic/v1/</c>。
    /// 换尾段换不出来，所以这里显式给——否则选 Claude 会拼出 <c>/inference/openai/v1/messages</c> 打 404
    /// </summary>
    /// <param name="format">目标 wire 格式</param>
    /// <returns>该线的完整端点</returns>
    public override string EndpointFor(EApiFormat format) => format switch
    {
        EApiFormat.Messages => "https://opencode.ai/inference/anthropic/v1/messages",
        EApiFormat.Responses => "https://opencode.ai/inference/openai/v1/responses",
        _ => "https://opencode.ai/inference/openai/v1/chat/completions",
    };

    /// <summary>
    /// opencode 系<b>强制</b>要 <c>x-opencode-session</c>：2026-09-05 起缺失该头的请求会被直接拒绝
    /// （400 MissingSessionID）。公共头 <c>X-Session-Id</c> 照发，这一条是它独有的、必须追加的。
    ///
    /// 值用会话亲和值（哈希）而不是内部会话 id——路由只认相等性，不必把磁盘目录名发给第三方。
    /// </summary>
    public override IReadOnlyList<string>? SessionAffinityHeaders => ["x-opencode-session"];

    /// <summary>body 字段没验证过它认不认，先不发——它会改字节，白付一次缓存失效</summary>
    public override IReadOnlyList<string>? SessionBodyFields => null;

    /// <summary>
    /// 网关下挂的模型各家不同，暂时沿用改动前的写法（<c>thinking.type</c> + <c>reasoning_effort</c>）。
    /// 是否被接受未验证，换格式前得先录一次真实请求。
    /// </summary>
    protected override EThinkingFormat DefaultThinkingFormat => EThinkingFormat.ThinkingType;
}
