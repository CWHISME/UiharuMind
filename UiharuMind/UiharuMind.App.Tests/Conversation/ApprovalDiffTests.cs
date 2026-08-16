using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Features.Conversation;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 审批卡片的 diff 是<b>干跑</b>出来的：读真实文件、跑与工具执行同一个纯函数，
/// 所以卡片上看到的就是落盘后的样子。以前是把 oldString/newString 两块裸文本对着摆，
/// 看不出改动落在文件哪个位置，等于逼人盲批。
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
