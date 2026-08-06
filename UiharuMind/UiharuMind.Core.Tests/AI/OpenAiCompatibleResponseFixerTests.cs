using System.Text;
using UiharuMind.Core.AI.Net;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 钉死兼容服务的 finish_reason 归一化:空串写回 null、未知值降级为 stop、合法值原样透传。
/// OpenAI SDK 对未知枚举值是直接抛异常的，这一层漏掉就会整轮对话失败。
/// </summary>
public class OpenAiCompatibleResponseFixerTests
{
    [Fact]
    public void EmptyFinishReason_BecomesNull()
    {
        const string json = """{"choices":[{"index":0,"delta":{"content":"hi"},"finish_reason":""}]}""";

        var fixedJson = OpenAiCompatibleResponseFixer.FixJson(json);

        Assert.NotNull(fixedJson);
        Assert.Contains("\"finish_reason\":null", fixedJson);
    }

    [Fact]
    public void UnknownFinishReason_FallsBackToStop()
    {
        const string json = """{"choices":[{"index":0,"finish_reason":"sensitive"}]}""";

        var fixedJson = OpenAiCompatibleResponseFixer.FixJson(json);

        Assert.NotNull(fixedJson);
        Assert.Contains("\"finish_reason\":\"stop\"", fixedJson);
    }

    [Theory]
    [InlineData("""{"choices":[{"index":0,"finish_reason":"stop"}]}""")]
    [InlineData("""{"choices":[{"index":0,"finish_reason":"tool_calls"}]}""")]
    [InlineData("""{"choices":[{"index":0,"finish_reason":null}]}""")]
    [InlineData("""{"choices":[{"index":0,"delta":{"content":"hi"}}]}""")]
    public void ValidPayload_IsNotRewritten(string json)
    {
        Assert.Null(OpenAiCompatibleResponseFixer.FixJson(json));
    }

    [Fact]
    public void BrokenJson_IsLeftAlone()
    {
        Assert.Null(OpenAiCompatibleResponseFixer.FixJson("""{"finish_reason":"""));
    }

    [Fact]
    public void EventStreamLine_KeepsDataPrefixAndPassesThroughDone()
    {
        var line = OpenAiCompatibleResponseFixer.FixEventStreamLine(
            """data: {"choices":[{"index":0,"finish_reason":""}]}""");

        Assert.StartsWith("data: ", line);
        Assert.Contains("\"finish_reason\":null", line);
        Assert.Equal("data: [DONE]", OpenAiCompatibleResponseFixer.FixEventStreamLine("data: [DONE]"));
        Assert.Equal("", OpenAiCompatibleResponseFixer.FixEventStreamLine(""));
    }

    [Fact]
    public async Task SanitizingStream_RewritesLinesAndKeepsBlankSeparators()
    {
        const string sse = "data: {\"choices\":[{\"index\":0,\"finish_reason\":\"\"}]}\n\ndata: [DONE]\n\n";
        await using var stream = new SseSanitizingStream(new MemoryStream(Encoding.UTF8.GetBytes(sse)));
        using var reader = new StreamReader(stream);

        var text = await reader.ReadToEndAsync();

        Assert.Equal("data: {\"choices\":[{\"index\":0,\"finish_reason\":null}]}\n\ndata: [DONE]\n\n", text);
    }
}
