using LiveMarkdown.Avalonia;
using UiharuMind.Shared.Controls;

namespace UiharuMind.App.Tests.Shared;

public class MarkdownChunkerTests
{
    private static List<string> Split(string text, int chunkChars) =>
        MarkdownChunker.Split(text, chunkChars, MarkdownUpdateProducer.DefaultPipeline);

    [Fact]
    public void Chunks_JoinBackToOriginal()
    {
        string text = string.Concat(Enumerable.Range(0, 50).Select(i => $"## 标题 {i}\n\n第 {i} 段正文，带 `code` 与 **粗体**。\n\n- 列表项 a\n- 列表项 b\n\n"));

        List<string> chunks = Split(text, 200);

        Assert.True(chunks.Count > 1);
        Assert.Equal(text, string.Concat(chunks));
    }

    [Fact]
    public void CodeFence_IsNeverCut()
    {
        string fence = "```csharp\n" + string.Concat(Enumerable.Range(0, 40).Select(i => $"var x{i} = {i};\n\n")) + "```\n";
        string text = "开头一段。\n\n" + fence + "\n结尾一段。\n";

        List<string> chunks = Split(text, 50);

        Assert.Contains(chunks, c => c.Contains(fence));
    }

    [Fact]
    public void ShortText_IsSingleChunk()
    {
        Assert.Single(Split("# 短文\n\n一段。", 4096));
        Assert.Empty(Split("", 4096));
    }
}
