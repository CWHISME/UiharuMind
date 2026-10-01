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

    /// <summary>商汤每块都带空串：按字面改写，正文原样（不经 DOM 重新转义），语义与 DOM 改写一致</summary>
    [Fact]
    public void EmptyFinishReason_IsRewrittenLiterally_KeepingContent()
    {
        const string json = """{"choices":[{"index":0,"delta":{"content":"说 \"finish_reason\":\"\" 也不误伤"},"finish_reason":""}]}""";

        var fixedJson = OpenAiCompatibleResponseFixer.FixJson(json);

        Assert.Equal("""{"choices":[{"index":0,"delta":{"content":"说 \"finish_reason\":\"\" 也不误伤"},"finish_reason":null}]}""", fixedJson);
    }

    /// <summary>字面快路径认不出的形态（带空格、混着未知值）仍由 DOM 兜住</summary>
    [Theory]
    [InlineData("""{"choices":[{"index":0,"finish_reason": ""}]}""", "\"finish_reason\":null")]
    [InlineData("""{"choices":[{"index":0,"finish_reason":""},{"index":1,"finish_reason":"sensitive"}]}""", "\"finish_reason\":\"stop\"")]
    public void UnusualFinishReasonShapes_StillFixed(string json, string expected)
    {
        var fixedJson = OpenAiCompatibleResponseFixer.FixJson(json);

        Assert.NotNull(fixedJson);
        Assert.Contains(expected, fixedJson);
        Assert.DoesNotContain("\"finish_reason\":\"\"", fixedJson);
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

        var text = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Equal("data: {\"choices\":[{\"index\":0,\"finish_reason\":null}]}\n\ndata: [DONE]\n\n", text);
    }
}
