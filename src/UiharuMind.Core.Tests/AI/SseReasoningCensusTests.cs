using System.Text;
using UiharuMind.Core.AI.Net.Response;
using UiharuMind.Core.AI.Net;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 思考字段普查：加密思考有没有下发，唯一的观测手段是这个计数——
/// 它数错了，「有思考 token、没有思考正文」那类模型的判读就会跟着错。
/// </summary>
public class SseReasoningCensusTests
{
    [Fact]
    public void PlainContent_IsCountedSeparatelyFromRenamedFields()
    {
        SseReasoningCensus census = Observe(
            """{"delta":{"reasoning_content":"想"}}""",
            """{"delta":{"reasoning_content":"一"}}""",
            """{"delta":{"reasoning":"这个名字要被改名"}}""",
            """{"delta":{"reasoning_text":"同一种"}}""");

        Assert.Equal("reasoning fields: reasoning_content=2, reasoning/reasoning_text=2, " +
                     "reasoning_details=0, encrypted=0", census.Describe());
    }

    /// <summary>结构化思考里有加密条目时，两者都要数到——这正是能否回填的分界</summary>
    [Fact]
    public void EncryptedDetails_AreCounted()
    {
        SseReasoningCensus census = Observe(
            """{"delta":{"reasoning_details":[{"type":"reasoning.encrypted","data":"opaque"}]}}""");

        Assert.Equal("reasoning fields: reasoning_content=0, reasoning/reasoning_text=0, " +
                     "reasoning_details=1, encrypted=1", census.Describe());
    }

    /// <summary>Kimi 方言的加密条目不带 reasoning. 前缀，且它的值键同名，只能按 type 数</summary>
    [Fact]
    public void KimiDialectEncryptedEntry_CountsOnce()
    {
        SseReasoningCensus census = Observe(
            """{"delta":{"reasoning_details":[{"type":"encrypted","encrypted":"opaque","index":0}]}}""");

        Assert.Equal("reasoning fields: reasoning_content=0, reasoning/reasoning_text=0, " +
                     "reasoning_details=1, encrypted=1", census.Describe());
    }

    /// <summary>正文里出现 encrypted 字样不算：加密条目只在 reasoning_details 里有意义</summary>
    [Fact]
    public void EncryptedOutsideReasoningDetails_IsNotCounted()
    {
        SseReasoningCensus census = Observe("""{"delta":{"content":"aes encrypted 是加密的意思"}}""");

        Assert.Equal("reasoning fields: reasoning_content=0, reasoning/reasoning_text=0, " +
                     "reasoning_details=0, encrypted=0", census.Describe());
    }

    private static SseReasoningCensus Observe(params string[] lines)
    {
        SseReasoningCensus census = new();
        foreach (string line in lines) census.Observe(Encoding.UTF8.GetBytes(line));
        return census;
    }
}
