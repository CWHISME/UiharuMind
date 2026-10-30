/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Net.Errors;

/// <summary>
/// 识别「历史里的助手消息必须带回思考」的 400：
/// <c>Missing required parameter: 'messages[8].reasoning_content'. This model needs the reasoning of
/// every earlier assistant message</c>。
///
/// 命中后由上层把该 ModelId 记进 <see cref="Configs.RemoteAI.BaseRemoteModelConfig.ReasoningEveryAssistantModelIds"/>
/// 并重发一次——重发是一次全新的 SDK 调用，请求体按新配置重新改写，每一条助手消息都会带上思考字段。
///
/// 为什么不预先按模型写表：这条约束<b>按模型</b>而不是按供应商/网关，同一网关下都不一致
/// （CommandCode 的 DeepSeek 不要求、beam 要求）。目录里几十个模型补不出表，
/// 等服务端亲口说一次比猜准（同 <see cref="MaxTokensErrorHandler"/> 的自愈口径）。
/// </summary>
internal static class ReasoningRequirementErrorHandler
{
    /// <summary>
    /// 是否为「缺思考字段」被服务端拒绝的错误。
    /// 判据：错误链里有消息同时提到 <c>reasoning_content</c> 与 <c>missing required parameter</c>——
    /// 只提到前者的是别的毛病（如「思考模式下带 tool_calls 必须回传」），那类由请求改写直接兜住。
    /// </summary>
    /// <param name="exception">顶层异常；沿 <see cref="Exception.InnerException"/> 逐层找</param>
    /// <returns>命中为 true</returns>
    public static bool IsRequirementError(Exception exception)
    {
        for (Exception? e = exception; e != null; e = e.InnerException)
        {
            string message = e.Message;
            if (message.Contains("reasoning_content", StringComparison.OrdinalIgnoreCase) &&
                message.Contains("missing required parameter", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
