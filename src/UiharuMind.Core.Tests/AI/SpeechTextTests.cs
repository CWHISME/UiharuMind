using UiharuMind.Core.AI.Audio;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 朗读前的文字整理：Markdown 记号、代码、链接地址、表情不念；按句切段，首段短；粗判语种挑模型
/// </summary>
public class SpeechTextTests
{
    [Fact]
    public void Clean_DropsMarkdownCodeLinksAndEmoji()
    {
        const string markdown = """
            <think>先想想</think>
            ## 结论 🎉
            这是 **重点**，详见[文档](https://example.com/doc)：
            - 第一条 `x = 1`
            > 引用

            ```csharp
            Console.WriteLine("不念");
            ```
            ![图](a.png) 地址 https://example.com 结束。
            """;

        string text = SpeechText.Clean(markdown);

        Assert.Equal("结论\n这是 重点，详见文档：\n第一条 x = 1\n引用\n地址 结束。", text);
    }

    [Fact]
    public void Split_KeepsFirstChunkShort_AndMergesShortSentences()
    {
        string text = "你好。" + string.Concat(Enumerable.Repeat("这是一个用来测试分段的句子。", 20));

        IReadOnlyList<string> chunks = SpeechText.Split(text);

        Assert.True(chunks[0].Length <= SpeechText.FirstChunkLength);
        Assert.All(chunks, x => Assert.True(x.Length <= SpeechText.MaxChunkLength));
        Assert.True(chunks[1].Length > 100); //短句并进同一段，不是一句一段
        Assert.Equal(text, string.Concat(chunks));
    }

    [Fact]
    public void Split_EnglishSentences_AndHardCutsOverlongOnes()
    {
        IReadOnlyList<string> chunks = SpeechText.Split("Hello there. " + new string('a', 400));

        Assert.Equal("Hello there.", chunks[0]);
        Assert.All(chunks, x => Assert.True(x.Length <= SpeechText.MaxChunkLength));
    }

    [Theory]
    [InlineData("今天天气不错，我们去公园吧。", "zh")]
    [InlineData("用 Python 写一个 hello world 程序。", "zh")] //中文夹英文仍算中文
    [InlineData("The quick brown fox jumps over the lazy dog.", "en")]
    [InlineData("今日はいい天気ですね。", "ja")]
    [InlineData("안녕하세요", "ko")]
    [InlineData("123 !!", null)]
    public void DetectLanguage(string text, string? expected)
    {
        Assert.Equal(expected, SpeechText.DetectLanguage(text));
    }
}
