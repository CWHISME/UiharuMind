using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 结果区的语言来源：结果形态由工具语义决定（白名单），不靠文案前缀猜——
/// 只有「结果=文件正文」的工具（Read）才按 FilePath 扩展名上色；
/// 回执类（Write）与非正文类一律纯文本，避免同一回执随扩展名忽有忽无像 bug
/// </summary>
public class ResultHighlightSourceTests
{
    [Fact]
    public void WriteReceipt_DoesNotHighlight()
    {
        var item = new ToolCallItem
        {
            ToolName = FileToolNames.Write,
            FilePath = "Foo.cs",
            ResultText = "Saved 'Foo.cs' (3 lines).",
        };

        Assert.Null(item.ResultHighlightSource);
    }

    [Fact]
    public void EditResult_DoesNotHighlight_InThisPath()
    {
        // Edit 的结果走 diff 面板（ResultDiffLines），纯文本结果区不是它的展示路径；
        // 名单里没有它，即使落进来也不上色
        var item = new ToolCallItem
        {
            ToolName = FileToolNames.Edit,
            FilePath = "Foo.cs",
            ResultText = "Applied 2 edits to Foo.cs.",
        };

        Assert.Null(item.ResultHighlightSource);
    }

    [Fact]
    public void ReadFileContent_HighlightsByExtension()
    {
        var item = new ToolCallItem
        {
            ToolName = FileToolNames.Read,
            FilePath = "Foo.cs",
            IsSuccess = true,
            ResultText = "public class A { }",
        };

        Assert.Equal("Foo.cs", item.ResultHighlightSource);
    }

    [Fact]
    public void ReadFailure_DoesNotHighlight()
    {
        // 失败结果是错误消息，不是文件正文
        var item = new ToolCallItem
        {
            ToolName = FileToolNames.Read,
            FilePath = "Foo.cs",
            IsSuccess = false,
            ResultText = "File not found",
        };

        Assert.Null(item.ResultHighlightSource);
    }

    [Fact]
    public void NoFilePath_DoesNotHighlight()
    {
        var item = new ToolCallItem { ToolName = FileToolNames.Read, ResultText = "public class A { }" };

        Assert.Equal(string.Empty, item.FilePath);
        Assert.Null(item.ResultHighlightSource);
    }

    [Theory]
    [InlineData(FileToolNames.Read, true)]
    [InlineData(FileToolNames.Write, false)]
    [InlineData(FileToolNames.Edit, false)]
    [InlineData(FileToolNames.Glob, false)]
    [InlineData(FileToolNames.Grep, false)]
    [InlineData("run_shell", false)]
    [InlineData("KnowledgeSearch", false)]
    public void IsFileContentTool_OnlyReadIsTrue(string toolName, bool expected)
    {
        Assert.Equal(expected, ToolCallItem.IsFileContentTool(toolName));
    }
}