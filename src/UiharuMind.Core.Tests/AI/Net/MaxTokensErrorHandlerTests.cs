using UiharuMind.Core.AI.Net;

namespace UiharuMind.Core.Tests.AI.Net;

public class MaxTokensErrorHandlerTests
{
    [Fact]
    public void UserReportedError_ParsesUpperBound()
    {
        // 实测报错原文:参数段与校验段分行,大小写混用
        var e = new Exception(
            "Agent turn failed: System.ClientModel.ClientResultException: HTTP 400 (invalid_request_error: 3)\n" +
            "Parameter: max_tokens\n\nfield MaxTokens invalid, should be in [1, 384000]");

        Assert.True(MaxTokensErrorHandler.TryGetUpperBound(e, out int bound));
        Assert.Equal(384000, bound);
    }

    [Fact]
    public void LowercaseMaxTokens_WithRange_ParsesUpperBound()
    {
        var e = new Exception("Invalid value for 'max_tokens': should be in [1, 8192]");

        Assert.True(MaxTokensErrorHandler.TryGetUpperBound(e, out int bound));
        Assert.Equal(8192, bound);
    }

    [Fact]
    public void NestedInnerException_IsFound()
    {
        var inner = new Exception("field MaxTokens invalid, should be in [1, 65536]");
        var outer = new InvalidOperationException("call failed",
            new HttpRequestException("transport error", inner));

        Assert.True(MaxTokensErrorHandler.TryGetUpperBound(outer, out int bound));
        Assert.Equal(65536, bound);
    }

    [Fact]
    public void OtherParameterError_IsNotMatched()
    {
        var e = new Exception("HTTP 400 (invalid_request_error): Parameter: temperature ... should be in [1, 2]");

        Assert.False(MaxTokensErrorHandler.TryGetUpperBound(e, out int bound));
        Assert.Equal(0, bound);
    }

    [Fact]
    public void MaxTokensError_WithoutRange_IsNotMatched()
    {
        // 措辞不是 "should be in [1, N]" 的(如 vLLM)不识别,宁可不自愈也不猜
        var e = new Exception("max_tokens must be less than or equal to 8192");

        Assert.False(MaxTokensErrorHandler.TryGetUpperBound(e, out _));
    }

    [Fact]
    public void UnrelatedError_IsNotMatched()
    {
        var e = new Exception("HTTP 401 Unauthorized");

        Assert.False(MaxTokensErrorHandler.TryGetUpperBound(e, out _));
    }
}