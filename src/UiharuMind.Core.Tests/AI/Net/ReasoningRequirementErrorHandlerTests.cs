using UiharuMind.Core.AI.Net;
using UiharuMind.Core.AI.Net.Errors;

namespace UiharuMind.Core.Tests.AI.Net;

/// <summary>
/// 「历史里的助手消息必须带回思考」的 400 识别：判据必须够准，误判会把「每条都带思考」
/// 永久记到模型配置上（虽然只是多发字段，但没必要）。
/// </summary>
public class ReasoningRequirementErrorHandlerTests
{
    private const string GatewayMessage =
        "Missing required parameter: 'messages[8].reasoning_content'. This model needs the reasoning of " +
        "every earlier assistant message: send back the 'reasoning_content' returned with that message.";

    [Fact]
    public void MatchesTheGatewayRejection()
    {
        Assert.True(ReasoningRequirementErrorHandler.IsRequirementError(new InvalidOperationException(GatewayMessage)));
    }

    /// <summary>错误常被包在外层异常里，沿 InnerException 也要认出来</summary>
    [Fact]
    public void LooksThroughInnerExceptions()
    {
        var wrapped = new InvalidOperationException("HTTP 400 (invalid_request_error: )",
            new HttpRequestException(GatewayMessage));

        Assert.True(ReasoningRequirementErrorHandler.IsRequirementError(wrapped));
    }

    /// <summary>DeepSeek 那条「思考模式下带 tool_calls 必须回传」不是这条：请求改写已经兜住，不该记配置</summary>
    [Fact]
    public void IgnoresToolCallReasoningRule()
    {
        var error = new InvalidOperationException(
            "If thinking mode and tool_calls, reasoning_content must be passed back to the API");

        Assert.False(ReasoningRequirementErrorHandler.IsRequirementError(error));
    }

    [Fact]
    public void IgnoresUnrelatedErrors()
    {
        Assert.False(ReasoningRequirementErrorHandler.IsRequirementError(
            new InvalidOperationException("Missing required parameter: 'messages[8].content'")));
        Assert.False(ReasoningRequirementErrorHandler.IsRequirementError(
            new InvalidOperationException("HTTP 429 Too Many Requests")));
    }
}
