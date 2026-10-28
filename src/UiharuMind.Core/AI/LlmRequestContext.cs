namespace UiharuMind.Core.AI;

/// <summary>
/// 随异步调用流传递的每请求上下文。
/// 发起一轮生成前由调用方设置,HTTP 层在改写请求体时读取——
/// 模型客户端由 OpenAI SDK 封装,没有逐请求参数通道,只能走 AsyncLocal。
/// </summary>
public static class LlmRequestContext
{
    private static readonly AsyncLocal<bool> _forbidToolCalls = new();

    /// <summary>
    /// 本次请求禁止调用工具（写入 <c>tool_choice: "none"</c>）。
    ///
    /// 用于写交接文档那种旁路请求：它要的是纯文本产出，但**工具定义必须照常带上**——
    /// 工具在请求体里紧跟 system 之后，缺了它请求前缀从一开始就与常规轮次岔开，
    /// 服务端的前缀缓存整个作废，而那一发恰好是最大的一次请求。
    ///
    /// 走 AsyncLocal 而不是 <c>ChatOptions</c>：MEAI 的 <c>ChatToolMode</c> 只有
    /// Auto / RequireAny / RequireSpecific，**没有 None**，抽象层表达不了「带着工具但不许调」。
    /// </summary>
    public static bool ForbidToolCalls
    {
        get => _forbidToolCalls.Value;
        set => _forbidToolCalls.Value = value;
    }

    private static readonly AsyncLocal<Func<IReadOnlyList<string?>?>?> _pendingReasoningSource = new();

    /// <summary>
    /// 本次请求历史里，<b>按助手消息出现顺序</b>排列的思考正文来源（缺思考的位置为 null）。
    ///
    /// 助手消息要带回当时的思考正文，但标准 ChatMessage→wire 消息转换认不出
    /// <c>TextReasoningContent</c>,序列化时会把它悄悄丢掉。丢失发生在 OpenAI SDK 内部、
    /// 早于 HTTP 层能碰到请求体之前,只能趁历史消息对象还在时
    /// (<see cref="Execution.LazyChatClient"/> 转发前)把它们挂在这里,让 HTTP 层照着回填。
    ///
    /// 按序而不是按 tool_call id:没有工具调用的普通回复同样要回传(网关按「每一条更早的助手消息」
    /// 校验),而它身上没有 id 可挂。顺序与即将发给 SDK 的那份消息列表一一对应。
    /// 给的是来源而不是现成的表:只有要求回填的模型才去收集,其余模型一次都不扫
    /// </summary>
    public static Func<IReadOnlyList<string?>?>? PendingReasoningSource
    {
        get => _pendingReasoningSource.Value;
        set => _pendingReasoningSource.Value = value;
    }
}
