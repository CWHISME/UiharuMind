using System.Text;
using UiharuMind.Core.AI.Reasoning;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 从响应字节里切加密思考票据。
///
/// 这组测试的来源是一次真实误报：deepseek 这种<b>明文</b>思考的模型，界面上突然冒出一张
/// 「加密思考」卡。根因是切票据的两步都太松——
/// ① 只看「有没有 <c>"reasoning_details"</c> 这个键」，不校验值是不是数组，
///    于是 <c>"reasoning_details":null</c> 这种每帧都有的形状会一路扫到同一帧的
///    <c>tool_calls</c> 数组，把工具参数当成票据；
/// ② 判「是不是加密」用的是裸的 <c>encrypted</c> 子串，撞上工具参数里的
///    <c>"path":"encrypted-notes.md"</c> 就命中。
/// 两步叠起来，一张不存在的票就进了历史，还被原样回填给服务端。
/// </summary>
public class ReasoningDetailExtractorTests
{
    private static bool TryTicket(string json, out string ticket)
    {
        bool found = ReasoningDetailExtractor.TryExtractTicket(Encoding.UTF8.GetBytes(json), out byte[] value);
        ticket = Encoding.UTF8.GetString(value);
        return found;
    }

    /// <summary>真正的加密条目：整块数组原样切下（不解析、不重编码）</summary>
    [Fact]
    public void RealEncryptedEntry_IsExtractedWhole()
    {
        const string frame =
            """{"choices":[{"delta":{"reasoning_details":[{"type":"reasoning.encrypted","data":"Q-PaD","id":"rs_1"}]}}]}""";

        Assert.True(TryTicket(frame, out string ticket));
        Assert.Equal("""[{"type":"reasoning.encrypted","data":"Q-PaD","id":"rs_1"}]""", ticket);
    }

    /// <summary>Kimi 方言的条目类型也叫 encrypted</summary>
    [Fact]
    public void DialectEncryptedEntry_IsRecognized()
    {
        const string frame = """{"delta":{"reasoning_details":[{"type":"encrypted","data":"BLOB"}]}}""";

        Assert.True(TryTicket(frame, out _));
    }

    /// <summary>
    /// 误报的源头：<c>reasoning_details</c> 是 null，同一帧后面还有 tool_calls。
    /// 旧实现会往后找第一个 <c>[</c>，把工具参数当成票据。
    /// </summary>
    [Fact]
    public void NullValue_DoesNotFallThroughToALaterArray()
    {
        const string frame =
            "{\"delta\":{\"reasoning_details\":null,\"tool_calls\":[{\"id\":\"c1\",\"function\":" +
            "{\"arguments\":\"{\\\"path\\\":\\\"encrypted-notes.md\\\"}\"}}]}}";

        Assert.False(ReasoningDetailExtractor.TryExtract(Encoding.UTF8.GetBytes(frame), out _));
        Assert.False(TryTicket(frame, out _));
    }

    /// <summary>值不是数组的其它形状同样不认（字符串、对象）</summary>
    [Theory]
    [InlineData("""{"delta":{"reasoning_details":"encrypted"}}""")]
    [InlineData("""{"delta":{"reasoning_details":{"type":"reasoning.encrypted"}}}""")]
    public void NonArrayValue_IsRejected(string frame)
    {
        Assert.False(TryTicket(frame, out _));
    }

    /// <summary>
    /// 只有摘要的数组不是票据。旧实现里非流式那条路连这一步都不判，
    /// 摘要数组会被当成票存进历史、下一发原样回填给服务端。
    /// </summary>
    [Fact]
    public void SummaryOnlyArray_IsNotATicket()
    {
        const string frame =
            """{"delta":{"reasoning_details":[{"type":"reasoning.summary","summary":"先看暂存区"}]}}""";

        Assert.True(ReasoningDetailExtractor.TryExtract(Encoding.UTF8.GetBytes(frame), out _));
        Assert.False(TryTicket(frame, out _));
    }

    /// <summary>
    /// 摘要正文里提到 encrypted 这个词也不算票据——判的是条目的 <c>type</c> 值，不是子串。
    /// 用户正在讨论这块代码时，模型复述一遍这个词是完全正常的。
    /// </summary>
    [Fact]
    public void SummaryMentioningTheWord_IsNotATicket()
    {
        const string frame =
            """{"delta":{"reasoning_details":[{"type":"reasoning.summary","summary":"要检查 encrypted 字段怎么回填"}]}}""";

        Assert.False(TryTicket(frame, out _));
    }

    /// <summary>空数组：切得出来，但不是票据</summary>
    [Fact]
    public void EmptyArray_IsNotATicket()
    {
        const string frame = """{"delta":{"reasoning_details":[]}}""";

        Assert.True(ReasoningDetailExtractor.TryExtract(Encoding.UTF8.GetBytes(frame), out _));
        Assert.False(TryTicket(frame, out _));
    }

    /// <summary>冒号后有空白也认（一次性响应体可能是排版过的 JSON）</summary>
    [Fact]
    public void WhitespaceAfterColon_IsTolerated()
    {
        const string frame = """{"delta":{"reasoning_details": [{"type":"reasoning.encrypted","data":"B"}]}}""";

        Assert.True(TryTicket(frame, out _));
    }
}
