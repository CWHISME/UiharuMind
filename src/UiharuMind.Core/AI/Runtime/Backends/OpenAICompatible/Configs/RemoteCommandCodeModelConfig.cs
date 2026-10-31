using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs.RemoteAI;

/// <summary>
/// Command Code（OpenAI/Anthropic 兼容聚合网关）。候选不写死变体表，
/// 走运行时模型目录（<see cref="ModelCatalogUrl"/>，ADR 0092）。
///
/// <para>三条线共用同一个前缀 <c>/provider/v1</c>（chat/completions、responses、messages，
/// 见 commandcode.ai/docs/provider），所以不覆写 <see cref="BaseRemoteModelConfig.EndpointFor"/>——
/// 换尾段就是对的。文档另有一条硬约束：<b>Claude 只能用 <c>/v1/messages</c></b>，
/// 发到 chat 或 responses 直接 400，与添加模型页「Claude 固定 messages」的规则一致。</para>
/// </summary>
[SettingConfigDesc("Command Code")]
[SettingConfigDesc("Command Code", LanguageUtils.ChineseSimplified)]
public class RemoteCommandCodeModelConfig : BaseRemoteModelConfig, IRemoteModelConfig
{
    public override string ModelName { get; set; } = "CommandCode";

    public override string ModelPath { get; set; } = "https://api.commandcode.ai/provider/v1/chat/completions";

    public override string WebsiteUrl { get; set; } = "https://commandcode.ai";

    public override string ModelCatalogUrl { get; set; } = "https://api.commandcode.ai/provider/v1/models";

    public override string ModelDescription { get; set; } = "";

    /// <summary>无默认模型：目录拉下来让用户挑</summary>
    public override string ModelId { get; set; } = "";

    public override int Port { get; set; }

    /// <summary>
    /// 网关下挂的模型各家不同，暂时沿用改动前的写法（<c>thinking.type</c> + <c>reasoning_effort</c>）。
    /// 这套参数在 CommandCode 上是否被接受仍未验证（ADR 0097），换格式前得先录一次真实请求。
    /// </summary>
    protected override EThinkingFormat DefaultThinkingFormat => EThinkingFormat.ThinkingType;

    /// <summary>
    /// 与 OpenRouter 同组：模型目录与 id 都是 OpenRouter 式命名（<c>meta/…</c>、<c>reflection/…</c>），
    /// 公共头 <c>X-Session-Id</c> 已经覆盖了它认的那一条，这里没有要追加的头。
    ///
    /// <b>已知上限</b>：打的是 <c>/provider/v1</c>，这条端点只认 <c>prompt_cache_key</c>，
    /// 官方明说「不保证 provider 端缓存命中」；带 <c>threadId</c> 的完整信封只在 <c>/alpha/generate</c>，
    /// 那条路我们走不到。若这一条上了命中率仍不动，就该认定是它的上游不缓存，客户端这边到此为止。
    /// </summary>
    public override IReadOnlyList<string>? SessionBodyFields => ["prompt_cache_key"];
}
