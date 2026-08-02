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
}
