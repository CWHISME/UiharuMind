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

    /// <summary>商汤自研模型的思考字段 reasoning → reasoning_content，SDK 只认后者</summary>
    [Theory]
    [InlineData("""{"choices":[{"index":0,"delta":{"reasoning":"用户让我"},"finish_reason":null}]}""", "delta")]
    [InlineData("""{"choices":[{"index":0,"message":{"role":"assistant","content":"hi","reasoning":"用户让我"},"finish_reason":"stop"}]}""", "message")]
    public void ReasoningField_IsRenamedToReasoningContent(string json, string container)
    {
        var fixedJson = OpenAiCompatibleResponseFixer.FixJson(json);

        Assert.NotNull(fixedJson);
        var node = System.Text.Json.Nodes.JsonNode.Parse(fixedJson);
        var obj = node!["choices"]![0]![container]!.AsObject();
        Assert.Equal("用户让我", obj["reasoning_content"]!.GetValue<string>());
        Assert.False(obj.ContainsKey("reasoning"));
    }

    /// <summary>空思考按字面删掉，不进 DOM；删完无事可做时返回删过的串</summary>
    [Theory]
    [InlineData("""{"choices":[{"index":0,"delta":{"reasoning":"","content":"hi"},"finish_reason":null}]}""",
        """{"choices":[{"index":0,"delta":{"content":"hi"},"finish_reason":null}]}""")]
    [InlineData("""{"choices":[{"index":0,"delta":{"content":"hi","reasoning":""},"finish_reason":null}]}""",
        """{"choices":[{"index":0,"delta":{"content":"hi"},"finish_reason":null}]}""")]
    public void EmptyReasoning_IsPrunedLiterally(string json, string expected)
    {
        Assert.Equal(expected, OpenAiCompatibleResponseFixer.FixJson(json));
    }

    /// <summary>已有 reasoning_content 时， stray reasoning 直接删掉，思考只留一个来源</summary>
    [Fact]
    public void Reasoning_IsDroppedWhenReasoningContentExists()
    {
        const string json = """{"choices":[{"index":0,"delta":{"reasoning":"旧的","reasoning_content":"新的"},"finish_reason":null}]}""";

        var fixedJson = OpenAiCompatibleResponseFixer.FixJson(json);

        Assert.NotNull(fixedJson);
        var delta = System.Text.Json.Nodes.JsonNode.Parse(fixedJson)!["choices"]![0]!["delta"]!.AsObject();
        Assert.Equal("新的", delta["reasoning_content"]!.GetValue<string>());
        Assert.False(delta.ContainsKey("reasoning"));
    }

    /// <summary>reasoning_content / reasoning_tokens 本来就是合法键，不能误伤；正文里讨论字段名也不行</summary>
    [Theory]
    [InlineData("""{"choices":[{"index":0,"delta":{"reasoning_content":"想"},"finish_reason":null}]}""")]
    [InlineData("""{"choices":[{"index":0,"delta":{},"finish_reason":"stop"}],"usage":{"completion_tokens_details":{"reasoning_tokens":236}}}""")]
    [InlineData("""{"choices":[{"index":0,"delta":{"content":"字段叫 \"reasoning\""},"finish_reason":null}]}""")]
    public void ReasoningLookalikes_AreNotRewritten(string json)
    {
        Assert.Null(OpenAiCompatibleResponseFixer.FixJson(json));
    }

    /// <summary>只有孤键 {"reasoning":""}（无逗号可字面删）时由 DOM 兜住</summary>
    [Fact]
    public void LoneEmptyReasoning_IsRemovedByDom()
    {
        const string json = """{"choices":[{"index":0,"delta":{"reasoning":""}}]}""";

        var fixedJson = OpenAiCompatibleResponseFixer.FixJson(json);

        Assert.Equal("""{"choices":[{"index":0,"delta":{}}]}""", fixedJson);
    }
}
