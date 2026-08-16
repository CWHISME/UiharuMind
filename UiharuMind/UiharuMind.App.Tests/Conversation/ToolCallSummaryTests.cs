using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 工具卡片折叠时那一行摘要。它是全场信息密度最高的一行——卡片没展开时，
/// 用户判断"这次调用干了什么"全靠它。
///
/// 两个坑都源于同一件事：<b>工具名与参数名改了，界面这侧没跟上</b>。
/// <c>filePath</c> 不在优先键里，于是文件工具全落到兜底分支，把参数原样摊开；
/// 图标认的还是 <c>file_access_</c> 前缀，于是文件工具一律显示通用扳手。
/// </summary>
public class ToolCallSummaryTests
{
    private static FunctionCallContent Call(string name, params (string Key, object? Value)[] args)
        => new("c1", name, args.ToDictionary(x => x.Key, x => x.Value));

    private static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    [Fact]
    public void EditCall_ShowsThePathAndTheEditCount()
    {
        FunctionCallContent call = Call(FileToolNames.Edit,
            ("filePath", "LineTest/MapTest.cs"),
            ("edits", Json("""[{"oldString":"a\nb","newString":"c"},{"oldString":"d","newString":"e"}]""")));

        string summary = AgentContentFormatter.SummarizeArguments(call);

        Assert.Equal("LineTest/MapTest.cs  (2 edits)", summary);
    }

    [Fact]
    public void SingleEdit_UsesTheSingularForm()
    {
        FunctionCallContent call = Call(FileToolNames.Edit,
            ("filePath", "A.cs"), ("edits", Json("""[{"oldString":"a","newString":"b"}]""")));

        Assert.Equal("A.cs  (1 edit)", AgentContentFormatter.SummarizeArguments(call));
    }

    [Fact]
    public void ReadCall_ShowsJustThePath()
    {
        Assert.Equal("src/A.cs",
            AgentContentFormatter.SummarizeArguments(Call(FileToolNames.Read, ("filePath", "src/A.cs"))));
    }

    /// <summary>
    /// 工作区内的绝对路径显示成相对路径：那串前缀对用户是已知信息，
    /// 而摘要那一列会把超出宽度的部分裁掉——前缀占满，文件名和编辑条数就都看不见了。
    /// </summary>
    [Fact]
    public void AbsolutePathInsideWorkspace_IsShownRelative()
    {
        string root = Path.Combine(Path.GetTempPath(), "ws");
        FunctionCallContent call = Call(FileToolNames.Edit,
            ("filePath", Path.Combine(root, "Assets", "MapTest.cs")),
            ("edits", Json("""[{"oldString":"a","newString":"b"}]""")));

        Assert.Equal("Assets/MapTest.cs  (1 edit)", AgentContentFormatter.SummarizeArguments(call, root));
    }

    /// <summary>越界写入保持绝对路径：<c>../../etc/hosts</c> 既难读，也更看不出它越界了</summary>
    [Fact]
    public void PathOutsideWorkspace_StaysAbsolute()
    {
        string root = Path.Combine(Path.GetTempPath(), "ws");

        string summary = AgentContentFormatter.SummarizeArguments(
            Call(FileToolNames.Write, ("filePath", "/etc/hosts")), root);

        Assert.Equal("/etc/hosts", summary);
    }

    /// <summary>路径过长时保<b>尾</b>：文件名比根目录前缀有信息量得多</summary>
    [Fact]
    public void OverlongPath_KeepsTheTail()
    {
        string path = "/Users/someone/Documents/UnityProjects/Works/SLG2/Client/UnityClient/Assets/MapTest.cs";

        string summary = AgentContentFormatter.SummarizeArguments(
            Call(FileToolNames.Read, ("filePath", path)));

        Assert.StartsWith("…", summary);
        Assert.EndsWith("Assets/MapTest.cs", summary);
        Assert.DoesNotContain("/Users/someone", summary);
    }

    /// <summary>摘要是一行:转义过的换行、真换行、过长的值都不该原样摆进去</summary>
    [Fact]
    public void LongOrMultilineValues_AreFlattenedAndTrimmed()
    {
        string summary = AgentContentFormatter.SummarizeArguments(
            Call("run_shell", ("command", "echo one\\ntwo\nthree")));
        Assert.Equal("echo one two three", summary);

        string longSummary = AgentContentFormatter.SummarizeArguments(
            Call("run_shell", ("command", new string('x', 200))));
        Assert.True(longSummary.Length < 100, $"过长的值应被截断,实际 {longSummary.Length} 字");
        Assert.EndsWith("…", longSummary);
    }

    /// <summary>认不出优先键时仍走兜底,但兜底的值也要过一遍收窄</summary>
    [Fact]
    public void UnknownArguments_FallBackToShortenedKeyValuePairs()
    {
        string summary = AgentContentFormatter.SummarizeArguments(
            Call("mystery_tool", ("alpha", new string('y', 200)), ("beta", 2), ("gamma", 3)));

        Assert.StartsWith("alpha: ", summary);
        Assert.Contains("beta: 2", summary);
        Assert.DoesNotContain("gamma", summary); //只取前两项
        Assert.Contains("…", summary);
    }

    [Theory]
    [InlineData(FileToolNames.Read)]
    [InlineData(FileToolNames.Write)]
    [InlineData(FileToolNames.Edit)]
    [InlineData(FileToolNames.Glob)]
    [InlineData(FileToolNames.Grep)]
    public void FileTools_GetTheFileIcon(string toolName)
    {
        Assert.Equal("📄", AgentContentFormatter.GetToolIcon(toolName));
    }

    [Fact]
    public void OtherTools_KeepTheirOwnIcons()
    {
        Assert.Equal("❯", AgentContentFormatter.GetToolIcon("run_shell"));
        Assert.Equal("🔧", AgentContentFormatter.GetToolIcon("mystery_tool"));
    }
}
