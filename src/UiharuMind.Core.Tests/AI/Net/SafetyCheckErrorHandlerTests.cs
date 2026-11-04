using System.Net;
using System.Net.Http;
using UiharuMind.Core.AI.Net.Errors;

namespace UiharuMind.Core.Tests.AI.Net;

/// <summary>
/// 上游安全检查拒绝的判据。实测原样（商汤）：
/// <c>HTTP 400 (failed_precondition_error: 9)\n\nsafety check failed</c>。
///
/// <para>认错一次就要白烧一整段上下文重发，所以判据卡得死：必须是 400，且正文里明说 safety check failed。</para>
/// </summary>
public class SafetyCheckErrorHandlerTests
{
    private static HttpRequestException BadRequest(string message) =>
        new(message, null, HttpStatusCode.BadRequest);

    [Fact]
    public void ReportedError_IsRecognized()
    {
        var e = BadRequest("HTTP 400 (failed_precondition_error: 9)\n\nsafety check failed");

        Assert.True(SafetyCheckErrorHandler.IsSafetyCheckFailure(e));
    }

    [Fact]
    public void NestedInnerException_IsFound()
    {
        var inner = BadRequest("safety check failed");
        var outer = new InvalidOperationException("turn failed", inner);

        Assert.True(SafetyCheckErrorHandler.IsSafetyCheckFailure(outer));
    }

    [Fact]
    public void BadRequest_WithoutSafetyWording_IsNotMatched()
    {
        // 其他 400 各自有别的毛病（max_tokens、缺字段），猜错就是白重发一次整段上下文
        var e = BadRequest("HTTP 400 (invalid_request_error: 3)\n\nInvalid value for 'max_tokens'");

        Assert.False(SafetyCheckErrorHandler.IsSafetyCheckFailure(e));
    }

    [Fact]
    public void SafetyWording_Without400_IsNotMatched()
    {
        // 状态码是这一类的必要条件：5xx 有 5xx 的处理路径（退避重发），不归这里
        var e = new HttpRequestException("safety check failed", null, HttpStatusCode.InternalServerError);

        Assert.False(SafetyCheckErrorHandler.IsSafetyCheckFailure(e));
    }
}
