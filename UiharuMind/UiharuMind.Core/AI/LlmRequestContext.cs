using UiharuMind.Core.AI.Models;

namespace UiharuMind.Core.AI;

/// <summary>
/// 随异步调用流传递的每请求上下文。
/// 发起一轮生成前由调用方设置,HTTP 层在改写请求体时读取——
/// 模型客户端由 OpenAI SDK 封装,没有逐请求参数通道,只能走 AsyncLocal。
/// </summary>
public static class LlmRequestContext
{
    private static readonly AsyncLocal<EThinkingMode> _thinkingMode = new();

    /// <summary>本次请求的思考力度(未设置为 Default,即沿用模型配置)</summary>
    public static EThinkingMode ThinkingMode
    {
        get => _thinkingMode.Value;
        set => _thinkingMode.Value = value;
    }

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
}
