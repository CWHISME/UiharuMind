using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 模型复读（如思考里无限输出「：**」）要在几千字内认出来，正常行文不能误判
/// </summary>
public class RepetitionLoopDetectorTests
{
    private static string Prose(int chars)
    {
        StringBuilder sb = new();
        for (int i = 0; sb.Length < chars; i++)
        {
            sb.Append($"第 {i} 步：检查 **SettingConfig.json** 的第 {i * 7 % 13} 项，");
            if (i % 5 == 4) sb.Append("\n\n");
        }

        return sb.ToString();
    }

    // 按小块喂，模拟流式增量；返回认出复读时已喂的字数，没认出为 -1
    private static int FeedInDeltas(RepetitionLoopDetector detector, string text, int deltaChars = 7)
    {
        for (int i = 0; i < text.Length; i += deltaChars)
        {
            string delta = text.Substring(i, Math.Min(deltaChars, text.Length - i));
            if (detector.Feed(delta) != null) return i + delta.Length;
        }

        return -1;
    }

    [Fact]
    public void ShortUnitLoop_IsCaughtWithinAFewThousandChars()
    {
        string prefix = Prose(3000);
        string loop = string.Concat(Enumerable.Repeat("：**", 20000));

        int caughtAt = FeedInDeltas(new RepetitionLoopDetector(), prefix + loop);

        Assert.InRange(caughtAt, prefix.Length + 1, prefix.Length + RepetitionLoopDetector.WindowChars + 512);
    }

    [Fact]
    public void LongerSentenceLoop_IsCaught()
    {
        string sentence = "等等，让我再确认一下配置文件是不是被覆盖了。\n";
        string loop = string.Concat(Enumerable.Repeat(sentence, 400));

        Assert.True(FeedInDeltas(new RepetitionLoopDetector(), Prose(2000) + loop) > 0);
    }

    [Fact]
    public void NormalProse_IsNotFlagged()
    {
        Assert.Equal(-1, FeedInDeltas(new RepetitionLoopDetector(), Prose(50000)));
    }

    [Fact]
    public void Watch_KeepsReasoningAndTextApart()
    {
        // 两路交替各出一半：混着喂会凑成周期，分开喂谁都不是复读
        RepetitionLoopWatch watch = new();
        for (int i = 0; i < 3000; i++)
        {
            watch.Observe(new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent($"想{i}")]));
            watch.Observe(new ChatResponseUpdate(ChatRole.Assistant, [new TextContent($"答{i}")]));
        }
    }

    [Fact]
    public void Watch_ThrowsOnReasoningLoop()
    {
        RepetitionLoopWatch watch = new();

        Assert.Throws<RepetitionLoopException>(() =>
        {
            for (int i = 0; i < 5000; i++)
                watch.Observe(new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("：**")]));
        });
    }
}
