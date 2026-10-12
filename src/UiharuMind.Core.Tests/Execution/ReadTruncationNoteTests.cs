using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>全文窗据此切开原文与附注：截断附注及其后的大纲、提示都不算原文</summary>
public class ReadTruncationNoteTests
{
    [Fact]
    public void ContentLineCount_StopsBeforeTheTruncationNote()
    {
        string result = "line1\nline2\n"
                        + ReadTruncationNote.Format(1, 2, "共 9 行", 3)
                        + "\n\n文件大纲（[起–止行] 结构，可传 offset 与 head 只读对应的段）：\n[1] # 标题";

        Assert.Equal(2, ReadTruncationNote.ContentLineCount(result));
    }

    [Fact]
    public void ContentLineCount_WithoutNote_IsWholeText()
    {
        Assert.Equal(0, ReadTruncationNote.ContentLineCount("line1\nline2"));
    }

    /// <summary>原文里恰好有一行附注字样（如读的是会话转写）时，不能把它当成附注</summary>
    [Fact]
    public void ContentLineCount_IgnoresNoteTextInsideContent()
    {
        string quoted = ReadTruncationNote.Format(1, 50, "共 90 行", 51);

        Assert.Equal(0, ReadTruncationNote.ContentLineCount("line1\n" + quoted + "\nline3"));
        Assert.Equal(3, ReadTruncationNote.ContentLineCount("line1\n" + quoted + "\nline3\n" + ReadTruncationNote.Format(1, 3, "共 9 行", 4)));
    }
}
