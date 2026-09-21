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

    [Fact]
    public void MultipleThinkBlocksAccumulate()
    {
        var result = Run("a<think>1</think>b<think>2</think>c");
        Assert.Equal("abc", result.Text);
        Assert.Equal("12", result.Thinking);
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
