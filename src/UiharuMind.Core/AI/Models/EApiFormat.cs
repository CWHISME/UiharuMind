namespace UiharuMind.Core.AI.Models;

/// <summary>
/// 远程模型答话的接口格式（wire）。v1 只实现 <see cref="ChatCompletions"/>；
/// <see cref="Messages"/> 供二期 Anthropic 线预留（ADR 0092）。
/// </summary>
public enum EApiFormat
{
    /// <summary>OpenAI 兼容的 /chat/completions</summary>
    ChatCompletions = 0,

    /// <summary>Anthropic Messages（/v1/messages）</summary>
    Messages = 1,
}
