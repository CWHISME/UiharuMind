using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 钉死行级 diff 的语义:编辑审批卡片靠它渲染变更,错一行就是误导审批。
/// </summary>
public class LineDiffTests
{
    [Fact]
    public void IdenticalTexts_AreAllContext()
    {
        var entries = LineDiff.Compute("a\nb", "a\nb");

        Assert.All(entries, x => Assert.Equal(ELineDiffKind.Context, x.Kind));
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void ChangedMiddleLine_YieldsRemovedThenAdded()
    {
        var entries = LineDiff.Compute("a\nold\nc", "a\nnew\nc");

        Assert.Equal(
        [
            new LineDiffEntry(ELineDiffKind.Context, "a"),
            new LineDiffEntry(ELineDiffKind.Removed, "old"),
            new LineDiffEntry(ELineDiffKind.Added, "new"),
            new LineDiffEntry(ELineDiffKind.Context, "c"),
        ], entries);
    }

    [Fact]
    public void EmptyOldText_IsAllAdded()
    {
        var entries = LineDiff.Compute("", "x\ny");

        Assert.All(entries, x => Assert.Equal(ELineDiffKind.Added, x.Kind));
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void CrlfAndLf_CompareEqual()
    {
        var entries = LineDiff.Compute("a\r\nb", "a\nb");

        Assert.All(entries, x => Assert.Equal(ELineDiffKind.Context, x.Kind));
    }

    [Fact]
    public void OversizedInput_FallsBackToBlockDiff_WithoutLosingContent()
    {
        string oldText = string.Join('\n', Enumerable.Range(1, 300).Select(i => $"o{i}"));
        string newText = string.Join('\n', Enumerable.Range(1, 300).Select(i => $"n{i}"));

        var entries = LineDiff.Compute(oldText, newText, maxLcsLines: 100);

        Assert.Equal(600, entries.Count);
        Assert.Equal(300, entries.Count(x => x.Kind == ELineDiffKind.Removed));
        Assert.Equal(300, entries.Count(x => x.Kind == ELineDiffKind.Added));
    }
}
