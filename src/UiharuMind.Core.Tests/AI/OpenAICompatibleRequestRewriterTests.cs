using System.Text;
using System.Text.Json.Nodes;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 请求改写直接在字节上做：根上的键按「保留的原样拷 + 要设的追加在末尾」重拼，
/// 内层只改要修的那几处，其余一字不动
/// </summary>
public class OpenAICompatibleRequestRewriterTests
{
    private static readonly IReadOnlyList<KeyValuePair<string, JsonNode?>> MaxTokens =
        [new("max_tokens", JsonValue.Create(65535))];

    /// <summary>
    /// 改写一段请求体
    /// </summary>
    /// <param name="json">请求体</param>
    /// <param name="rewrite">要做的改写</param>
    /// <returns>改写结果；没改写时为 null</returns>
    internal static string? Rewrite(string json, RequestRewrite rewrite)
    {
        using PooledByteWriter output = new(json.Length);
        return OpenAICompatibleRequestRewriter.Rewrite(Encoding.UTF8.GetBytes(json), rewrite, output)
            ? Encoding.UTF8.GetString(output.WrittenSpan)
            : null;
    }

    [Fact]
    public void ExtraParams_OverwriteExistingKeys_AndMoveThemToTheEnd()
    {
        string? rewritten = Rewrite("""{"max_tokens":5,"model":"m","messages":[]}""", new(MaxTokens, false, null, false));

        Assert.Equal("""{"model":"m","messages":[],"max_tokens":65535}""", rewritten);
    }

    [Fact]
    public void ExtraParams_OnAnEmptyObject()
    {
        Assert.Equal("""{"max_tokens":65535}""", Rewrite("{}", new(MaxTokens, false, null, false)));
    }

    [Fact]
    public void ObjectValuedExtraParam_IsWrittenCompactWithoutEscapingNonAscii()
    {
        IReadOnlyList<KeyValuePair<string, JsonNode?>> thinking =
            [new("thinking", new JsonObject { ["type"] = "enabled", ["note"] = "中文" })];

        Assert.Equal("""{"model":"m","thinking":{"type":"enabled","note":"中文"}}""",
            Rewrite("""{"model":"m"}""", new(thinking, false, null, false)));
    }

    [Fact]
    public void OmitSamplingParams_RemovesOnlySamplingKeysAtTheRoot()
    {
        string? rewritten = Rewrite(
            """{"temperature":0.7,"model":"m","top_p":0.9,"tools":[{"parameters":{"temperature":{"type":"number"}}}],"presence_penalty":0.5,"frequency_penalty":0.5,"max_tokens":1}""",
            new(null, false, null, true));

        // 工具参数里同名的键不是采样参数，不能动
        Assert.Equal("""{"model":"m","tools":[{"parameters":{"temperature":{"type":"number"}}}],"max_tokens":1}""", rewritten);
    }

    [Fact]
    public void NullArguments_BecomeEmptyObject_ButTextMentioningThemDoesNot()
    {
        string? rewritten = Rewrite(
            """{"messages":[{"role":"user","content":"说 \"arguments\":\"null\" 不该改"},{"role":"assistant","tool_calls":[{"id":"c1","function":{"name":"f","arguments":"null"}}]}]}""",
            default);

        Assert.Equal(
            """{"messages":[{"role":"user","content":"说 \"arguments\":\"null\" 不该改"},{"role":"assistant","tool_calls":[{"id":"c1","function":{"name":"f","arguments":"{}"}}]}]}""",
            rewritten);
    }

    [Fact]
    public void Reasoning_IsInsertedAtTheEndOfTheMessage_EscapedLikeTheSdk()
    {
        var reasoning = new Dictionary<string, string> { ["c1"] = "想<一>&\"引号\"\n第二行" };

        string? rewritten = Rewrite(
            """{"messages":[{"role":"assistant","tool_calls":[{"id":"c1","type":"function"}]}]}""",
            new(null, false, reasoning, false));

        Assert.Equal(
            """{"messages":[{"role":"assistant","tool_calls":[{"id":"c1","type":"function"}],"reasoning_content":"想<一>&\"引号\"\n第二行"}]}""",
            rewritten);
    }

    /// <summary>
    /// 回填在消息收尾时才记下，而 <c>"reasoning_content":null</c> 排在 tool_calls 之前：
    /// 两处改动记录顺序与位置顺序相反，拼的时候必须按位置来
    /// </summary>
    [Fact]
    public void NullReasoningBeforeToolCalls_WithArgumentFix_BothApply()
    {
        var reasoning = new Dictionary<string, string> { ["c1"] = "思考" };

        string? rewritten = Rewrite(
            """{"messages":[{"role":"assistant","reasoning_content":null,"tool_calls":[{"id":"c1","function":{"arguments":"null"}}]}]}""",
            new(null, false, reasoning, false));

        Assert.Equal(
            """{"messages":[{"role":"assistant","reasoning_content":"思考","tool_calls":[{"id":"c1","function":{"arguments":"{}"}}]}]}""",
            rewritten);
    }

    /// <summary>额外参数整个覆盖掉 messages 时，原来那份不能再扫：记下的改动会落到别的属性上</summary>
    [Fact]
    public void ExtraParamOverridingMessages_WithArgumentFix_ReplacesThemCleanly()
    {
        IReadOnlyList<KeyValuePair<string, JsonNode?>> messages = [new("messages", new JsonArray())];
        var reasoning = new Dictionary<string, string> { ["c1"] = "思考" };

        string? rewritten = Rewrite(
            """{"messages":[{"role":"assistant","tool_calls":[{"id":"c1","function":{"arguments":"null"}}]}],"model":"m"}""",
            new(messages, false, reasoning, false));

        Assert.Equal("""{"model":"m","messages":[]}""", rewritten);
    }

    [Theory]
    [InlineData("[1,2,3]")]
    [InlineData("\"text\"")]
    [InlineData("""{"model":"m",""")]
    [InlineData("not json")]
    public void NonObjectOrBrokenBody_IsLeftAlone(string json)
    {
        Assert.Null(Rewrite(json, new(MaxTokens, true, null, false)));
    }
}
