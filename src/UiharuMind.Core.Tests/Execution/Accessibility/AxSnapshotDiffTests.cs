using UiharuMind.Core.AI.Execution.Tools.Accessibility;

namespace UiharuMind.Core.Tests.Execution.Accessibility;

/// <summary>
/// 只给变了的行，前提是结构没变（编号才不会错位）；变得多了不如整页重给
/// </summary>
public class AxSnapshotDiffTests
{
    private static readonly string Page = string.Join('\n', Enumerable.Range(1, 10).Select(i => $"- textbox \"F{i}\" [ref=e{i}]"));

    [Fact]
    public void IdenticalSnapshots_HaveNoChanges()
    {
        Assert.Equal(string.Empty, AxSnapshotDiff.ChangedLines(Page, Page));
    }

    [Fact]
    public void AFewInPlaceChanges_AreListedWithTheirNewContent()
    {
        string after = Page.Replace("[ref=e3]", "[ref=e3]: filled");

        Assert.Equal("- textbox \"F3\" [ref=e3]: filled", AxSnapshotDiff.ChangedLines(Page, after));
    }

    [Fact]
    public void StructuralOrWidespreadChanges_NeedTheWholePage()
    {
        Assert.Null(AxSnapshotDiff.ChangedLines(Page, Page + "\n- button \"New\" [ref=e11]"));
        Assert.Null(AxSnapshotDiff.ChangedLines(Page, Page.Replace("textbox", "searchbox")));
    }
}
