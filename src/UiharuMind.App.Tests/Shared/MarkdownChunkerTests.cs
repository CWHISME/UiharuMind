using LiveMarkdown.Avalonia;
using UiharuMind.Shared.Controls;

namespace UiharuMind.App.Tests.Shared;

public class MarkdownChunkerTests
{
    private static List<string> Split(string text, int chunkChars) =>
        SplitChunks(text, chunkChars, int.MaxValue).Select(c => c.Text).ToList();

    private static List<MarkdownChunk> SplitChunks(string text, int chunkChars, int maxParagraphChars) =>
        MarkdownChunker.Split(text, chunkChars, maxParagraphChars, MarkdownUpdateProducer.DefaultPipeline);

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

    [Fact]
    public void DeeplyNestedInput_FallsBackWithoutThrowing()
    {
        // 1000 层嵌套引用：Markdig 直接抛“too deeply nested”，预览不能跟着炸
        string text = string.Concat(Enumerable.Repeat("> ", 1000)) + "x\n"
            + string.Concat(Enumerable.Range(0, 50).Select(i => $"第 {i} 段凑长度。\n\n"));

        List<string> chunks = Split(text, 200);

        Assert.Equal(text, string.Concat(chunks));
    }

    [Fact]
    public void OversizedParagraph_IsCutAndMarked()
    {
        // 模型复读出的超长单段：整块渲染会冻住界面，须硬切成纯文本段，前后正常块不受牵连。
        // 复读够长时整篇行内解析会撞嵌套深度上限抛异常，切段不能因此退回不分青红皂白的兜底
        string loop = "复读开始" + string.Concat(Enumerable.Repeat("：**", 30000)) + "\n";
        string text = "# 标题\n\n正常一段。\n\n" + loop + "\n结尾一段。\n";

        List<MarkdownChunk> chunks = SplitChunks(text, 200, 1000);

        Assert.Equal(text, string.Concat(chunks.Select(c => c.Text)));
        List<MarkdownChunk> oversized = chunks.Where(c => c.IsOversizedBlock).ToList();
        Assert.True(oversized.Count > 1);
        Assert.All(oversized, c => Assert.True(c.Text.Length <= 2048, $"段长 {c.Text.Length}"));
        Assert.Contains(chunks, c => !c.IsOversizedBlock && c.Text.Contains("正常一段"));
        Assert.Contains(chunks, c => !c.IsOversizedBlock && c.Text.Contains("结尾一段"));
    }

    [Fact]
    public void LongCodeFence_StaysWhole()
    {
        string fence = "```\n" + string.Concat(Enumerable.Range(0, 40).Select(i => $"var x{i} = {i};\n")) + "```\n";
        string text = "开头一段。\n\n" + fence + "\n结尾一段。\n";

        List<MarkdownChunk> chunks = SplitChunks(text, 50, 100);

        Assert.Contains(chunks, c => !c.IsOversizedBlock && c.Text.Contains(fence));
    }
}
