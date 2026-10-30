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

    /// <summary>
    /// OpenAI Responses（/v1/responses）。
    ///
    /// 与 <see cref="ChatCompletions"/> 的差别不只是路径：请求体是 <c>input</c> item 数组
    /// 而不是 <c>messages</c>，响应是逐类 SSE 事件而不是 <c>delta</c>。
    /// 加密思考票据在两条路上的承载也不同——chat 挂消息上（<c>reasoning_details</c>），
    /// responses 挂 item 上（<c>encrypted_content</c>）。
    /// </summary>
    Responses = 2,
}
