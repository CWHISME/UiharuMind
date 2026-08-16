using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 编辑改动的两处呈现，都必须看得出改了什么：
///
/// <b>审批卡片</b>（执行前）的 diff 是<b>干跑</b>出来的——读真实文件、跑与工具执行同一个纯函数，
/// 所以卡片上看到的就是落盘后的样子。以前是把 oldString/newString 两块裸文本对着摆，
/// 看不出改动落在文件哪个位置，等于逼人盲批。
///
/// <b>工具调用卡片</b>（执行后）的 diff 是把渲染过的结果正文认回来染色的——文件已经改了，
/// 没法再干跑。自动放行档位下这张卡片是唯一看得到改动的地方。
/// </summary>
public class ApprovalDiffTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uiharu-diff-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // 临时目录清理失败不影响断言
        }
    }

    private static FunctionCallContent EditCall(string filePath, params (string Old, string New)[] edits)
    {
        // 模型给的参数是 JSON，卡片拿到的也是 JsonElement——测试必须走同一条路
        string json = JsonSerializer.Serialize(
            edits.Select(e => new { oldString = e.Old, newString = e.New }));
        return new FunctionCallContent("c1", "Edit", new Dictionary<string, object?>
        {
            ["filePath"] = filePath,
            ["edits"] = JsonSerializer.Deserialize<JsonElement>(json),
        });
    }

    [Fact]
    public void EditCall_RendersRealDiffWithLineNumbers()
    {
        string path = Path.Combine(_dir, "A.cs");
        File.WriteAllText(path, "l1\nl2\nl3\nold\nl5\nl6\n");

        var lines = DiffLineView.BuildForToolCall(EditCall(path, ("old", "new")));

        Assert.Contains(lines, x => x.IsRemoved && x.Text.Contains("old") && x.Text.Contains("4"));
        Assert.Contains(lines, x => x.IsAdded && x.Text.Contains("new") && x.Text.Contains("4"));
        Assert.Contains(lines, x => !x.IsAdded && !x.IsRemoved && x.Text.Contains("l3")); //带上下文
    }

    [Fact]
    public void RelativePath_IsResolvedAgainstTheWorkspaceRoot()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "src"));
        File.WriteAllText(Path.Combine(_dir, "src", "B.cs"), "alpha\n");

        var lines = DiffLineView.BuildForToolCall(EditCall("src/B.cs", ("alpha", "beta")), _dir);

        Assert.Contains(lines, x => x.IsAdded && x.Text.Contains("beta"));
    }

    /// <summary>
    /// 预演判定「这次编辑必然失败」时静默回退成参数摘要：那种失败在自动编辑档下压根弹不出卡片，
    /// 为它单独做一条红字告警，收益抵不上多养一条 UI 分支（见 ADR 0007）。
    /// </summary>
    [Fact]
    public void UnappliableEdit_FallsBackToNoDiff()
    {
        string path = Path.Combine(_dir, "C.cs");
        File.WriteAllText(path, "dup\ndup\n");

        Assert.Empty(DiffLineView.BuildForToolCall(EditCall(path, ("nowhere", "x")))); //匹配不到
        Assert.Empty(DiffLineView.BuildForToolCall(EditCall(path, ("dup", "x")))); //匹配不唯一
    }

    [Fact]
    public void MissingFileOrUnresolvableRelativePath_FallsBackToNoDiff()
    {
        Assert.Empty(DiffLineView.BuildForToolCall(
            EditCall(Path.Combine(_dir, "gone.cs"), ("a", "b"))));
        Assert.Empty(DiffLineView.BuildForToolCall(EditCall("src/B.cs", ("a", "b")))); //没有工作目录可拼
    }

    /// <summary>
    /// 工具卡片的 diff 是把<b>渲染过的结果正文</b>认回来染色的（执行之后文件已改，没法再干跑）。
    /// 渲染与解析是同一份代码的两端，这条往返测试就是那层耦合的保险：
    /// 哪天改了 RenderDiff 的格式，这里会红，而不是界面上悄悄变回一片灰字。
    /// </summary>
    [Fact]
    public void RenderedToolResult_RoundTripsBackIntoColouredLines()
    {
        string path = Path.Combine(_dir, "D.cs");
        File.WriteAllText(path, "l1\nl2\nold\nl4\n");
        FileEditPlan plan = FileEditPlanner.Plan(File.ReadAllText(path), "D.cs",
            [new FileEdit { OldString = "old", NewString = "new" }]);
        Assert.True(plan.Succeeded, plan.Error);

        // 与 Edit 工具返回给模型的正文逐字一致
        string result = $"Applied 1 edit(s) to 'D.cs'.\n{FileEditPlanner.RenderDiff(plan.Diff, 80)}";

        var lines = DiffLineView.ParseToolResult(result);

        Assert.Contains(lines, x => x.IsRemoved && x.Text.Contains("old"));
        Assert.Contains(lines, x => x.IsAdded && x.Text.Contains("new"));
        Assert.Contains(lines, x => !x.IsAdded && !x.IsRemoved && x.Text.StartsWith("Applied 1 edit"));
        Assert.Equal(1, lines.Count(x => x.IsRemoved));
        Assert.Equal(1, lines.Count(x => x.IsAdded));
    }

    [Theory]
    [InlineData("")]
    [InlineData("File 'x.cs' not found.")]
    [InlineData("[Edit failed] edits[0].oldString was not found in 'x.cs'.")]
    [InlineData("l1\nl2\nl3")] //Read 的输出:没有前缀行号，不该被认成 diff
    public void NonEditResults_AreLeftAsPlainText(string resultText)
    {
        Assert.Empty(DiffLineView.ParseToolResult(resultText));
    }

    [Fact]
    public void ToolCallItem_PicksUpTheDiffWhenTheResultArrives()
    {
        ToolCallItem item = new() { ToolName = "Edit" };
        Assert.False(item.HasResultDiff);

        item.ResultText = "Applied 1 edit(s) to 'x.cs'.\n- 3 old\n+ 3 new";

        Assert.True(item.HasResultDiff);
        Assert.Contains(item.ResultDiffLines, x => x.IsAdded && x.Text.Contains("new"));
    }

    [Fact]
    public void WriteCall_StillRendersEveryLineAsAdded()
    {
        FunctionCallContent call = new("c2", "Write", new Dictionary<string, object?>
        {
            ["filePath"] = Path.Combine(_dir, "New.cs"),
            ["content"] = "x\ny\n",
        });

        var lines = DiffLineView.BuildForToolCall(call);

        Assert.All(lines.Where(x => x.Prefix != " "), x => Assert.True(x.IsAdded));
        Assert.Contains(lines, x => x.IsAdded && x.Text == "x");
    }
}
