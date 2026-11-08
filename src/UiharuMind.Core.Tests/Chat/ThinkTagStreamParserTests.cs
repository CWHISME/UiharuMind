using System.Text;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 钉死流式 &lt;think&gt; 解析的两条关键性质:
/// ① 标签被增量任意切断("&lt;thi"+"nk&gt;")也能正确分离;
/// ② 疑似半个标签的正文(如末尾孤立的 "&lt;")不会被吞掉,Complete 时原样放出。
/// </summary>
public class ThinkTagStreamParserTests
{
    private static (string Text, string Thinking) Run(params string[] deltas)
    {
        ThinkTagStreamParser parser = new();
        StringBuilder text = new();
        StringBuilder thinking = new();
        foreach (string delta in deltas)
        {
            parser.Feed(delta, x => text.Append(x), x => thinking.Append(x));
        }

        parser.Complete(x => text.Append(x), x => thinking.Append(x));
        return (text.ToString(), thinking.ToString());
    }

    [Fact]
    public void PlainTextPassesThrough()
    {
        var result = Run("hello ", "world");
        Assert.Equal("hello world", result.Text);
        Assert.Equal("", result.Thinking);
    }

    [Fact]
    public void ThinkBlockIsSeparated()
    {
        var result = Run("<think>ponder</think>answer");
        Assert.Equal("answer", result.Text);
        Assert.Equal("ponder", result.Thinking);
    }

    [Fact]
    public void TagSplitAcrossDeltasIsRecognized()
    {
        var result = Run("<thi", "nk>pon", "der</th", "ink>ans", "wer");
        Assert.Equal("answer", result.Text);
        Assert.Equal("ponder", result.Thinking);
    }

    /// <summary>
    /// 段首相邻的两段内联思考都算（中间没有可见正文上屏）。
    /// </summary>
    [Fact]
    public void AdjacentThinkBlocksAtTheStartBothCount()
    {
        var result = Run("<think>1</think><think>2</think>c");
        Assert.Equal("c", result.Text);
        Assert.Equal("12", result.Thinking);
    }

    /// <summary>
    /// 正文里引用了标签（实机事故的形状）：一旦有正文上屏，后面的 &lt;think&gt; 一律按字面量。
    /// 从前它会被当成真的开标签，把这句话之后的所有正文吞进思考卡——实机一条 5620 字的回答
    /// 被切在第 829 字，后 4784 字成了「思考」；同一轮里标签被引用几十次就被切成几十块。
    /// </summary>
    [Fact]
    public void ThinkTagQuotedInTextStaysLiteral()
    {
        const string answer = "实测（400 条 <think> 混排：1.7 ms）";

        var result = Run(answer);

        Assert.Equal(answer, result.Text);
        Assert.Equal("", result.Thinking);
    }

    /// <summary>
    /// 引用的一对标签也不能当开关：正文里的 &lt;think&gt; 与 &lt;/think&gt; 轮番出现时，
    /// 从前会把两边之间的正文全部抽进思考卡（「回答被切碎」正是这个形状）。
    /// </summary>
    [Fact]
    public void QuotedTagPairInTextStaysLiteral()
    {
        const string answer = "看这里 `</think>` 与 `<think>` 两处引用";

        var result = Run(answer);

        Assert.Equal(answer, result.Text);
        Assert.Equal("", result.Thinking);
    }

    /// <summary>
    /// 段边界（一次服务调用结束、工具调用）之后是下一条消息的开头：开标签重新算数。
    /// 这条守着「只在段首认」不能把本地模型带工具的那条路一起关死。
    /// </summary>
    [Fact]
    public void TagCountsAgainAfterReset()
    {
        ThinkTagStreamParser parser = new();
        StringBuilder text = new();
        StringBuilder thinking = new();
        void Feed(string delta) => parser.Feed(delta, x => text.Append(x), x => thinking.Append(x));

        Feed("先说一句 <think> 这种引用"); //有正文上屏后标签按字面量
        parser.Reset(); //段边界（与 ThinkingBoundary.Dispatch 同一处理）
        Feed("<think>再想</think>结论"); //新的一条消息:开头仍是真标签

        Assert.Equal("先说一句 <think> 这种引用结论", text.ToString());
        Assert.Equal("再想", thinking.ToString());
    }

    [Fact]
    public void UnterminatedThinkFlushedAsThinking()
    {
        var result = Run("<think>never closed");
        Assert.Equal("", result.Text);
        Assert.Equal("never closed", result.Thinking);
    }

    [Fact]
    public void TrailingPartialTagFlushedAsText()
    {
        // 末尾孤立的 "<th" 疑似标签开头被扣留,Complete 时按正文放出,不能被吞
        var result = Run("1 < 2 and <th");
        Assert.Equal("1 < 2 and <th", result.Text);
        Assert.Equal("", result.Thinking);
    }

    [Fact]
    public void AngleBracketInsideTextIsNotEaten()
    {
        var result = Run("a<b ", "and c<d");
        Assert.Equal("a<b and c<d", result.Text);
        Assert.Equal("", result.Thinking);
    }
}
