using UiharuMind.Core.AI.Net;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 钉死 tool_calls 里 type 的归一化：兼容服务会返回空的 type，
/// 而 OpenAI SDK 解析枚举时直接抛 "Unknown ChatToolCallKind value."，整轮对话当场失败。
///
/// 承重之处是**只在 tool_calls 数组的元素上认这个键**——type 是个到处都有的键名，
/// 全局匹配会把消息体、内容块里的 type 一起改坏。
/// </summary>
public class OpenAiCompatibleToolCallKindTests
{
    [Fact]
    public void EmptyToolCallType_BecomesFunction()
    {
        const string json =
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"c1","type":"","function":{"name":"grep"}}]}}]}""";

        var fixedJson = OpenAiCompatibleResponseFixer.FixJson(json);

        Assert.NotNull(fixedJson);
        Assert.Contains("\"type\":\"function\"", fixedJson);
    }

    [Fact]
    public void UnknownToolCallType_BecomesFunction()
    {
        const string json = """{"choices":[{"delta":{"tool_calls":[{"index":0,"type":"custom_tool"}]}}]}""";

        var fixedJson = OpenAiCompatibleResponseFixer.FixJson(json);

        Assert.NotNull(fixedJson);
        Assert.Contains("\"type\":\"function\"", fixedJson);
        Assert.DoesNotContain("custom_tool", fixedJson);
    }

    [Fact]
    public void ValidToolCall_IsLeftAlone()
    {
        const string json =
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"type":"function","function":{"name":"grep"}}]}}]}""";

        Assert.Null(OpenAiCompatibleResponseFixer.FixJson(json)); //没改动就不该重建 JSON
    }

    [Fact]
    public void MissingType_IsNotInvented()
    {
        //流式后续 chunk 本来就只带 index 与 arguments 增量,硬塞 type 是在改协议语义
        const string json =
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"q\":"}}]}}]}""";

        Assert.Null(OpenAiCompatibleResponseFixer.FixJson(json));
    }

    [Fact]
    public void TypeOutsideToolCalls_IsNeverTouched()
    {
        const string json =
            """{"choices":[{"delta":{"content":[{"type":"text","text":"hi"}],"tool_calls":[{"index":0,"type":""}]}}]}""";

        var fixedJson = OpenAiCompatibleResponseFixer.FixJson(json);

        Assert.NotNull(fixedJson);
        Assert.Contains("\"type\":\"text\"", fixedJson); //内容块的 type 必须原封不动
        Assert.Contains("\"type\":\"function\"", fixedJson);
    }

    [Fact]
    public void BothFixesApplyInOneResponse()
    {
        const string json =
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"type":""}]},"finish_reason":""}]}""";

        var fixedJson = OpenAiCompatibleResponseFixer.FixJson(json);

        Assert.NotNull(fixedJson);
        Assert.Contains("\"type\":\"function\"", fixedJson);
        Assert.Contains("\"finish_reason\":null", fixedJson);
    }
}
