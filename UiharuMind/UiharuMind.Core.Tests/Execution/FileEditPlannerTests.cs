using System.Text;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 钉死编辑语义。这套语义是「模型改代码不改坏」的全部依靠：
/// 匹配必须唯一（不唯一就交回给模型加上下文，绝不替它猜一处）、重叠必须报错、
/// 失败必须整体不落盘、未命中的字节必须一个都不动。
///
/// 保守 fuzzy 的边界也在这里钉：吸收行尾空白与 CRLF/LF，<b>但绝不碰全角标点</b>——
/// 本仓注释通篇是带全角标点的中文，一次静默改写就是几十行无关变更（见 ADR 0007）。
/// </summary>
public class FileEditPlannerTests
{
    private static FileEditPlan Plan(string text, params (string Old, string New)[] edits)
        => FileEditPlanner.Plan(text, "x.cs",
            edits.Select(e => new FileEdit { OldString = e.Old, NewString = e.New }).ToList());

    private static string NewTextOf(FileEditPlan plan)
    {
        Assert.True(plan.Succeeded, plan.Error);
        // NewText 是 internal，测试项目通过 InternalsVisibleTo 看得见
        return plan.NewText;
    }

    [Fact]
    public void SingleEdit_ReplacesTheMatch()
    {
        FileEditPlan plan = Plan("a\nold\nc\n", ("old", "new"));

        Assert.Equal("a\nnew\nc\n", NewTextOf(plan));
    }

    [Fact]
    public void MultipleEdits_AllApplyInOneCall()
    {
        FileEditPlan plan = Plan("one\ntwo\nthree\n", ("one", "1"), ("three", "3"));

        Assert.Equal("1\ntwo\n3\n", NewTextOf(plan));
    }

    /// <summary>
    /// 每条 edit 都对着<b>原文</b>匹配，不是对着前几条的结果——
    /// 否则模型必须在脑子里模拟中间态，那是它最容易算错的一步
    /// </summary>
    [Fact]
    public void EveryEdit_MatchesAgainstTheOriginalText()
    {
        FileEditPlan plan = Plan("x = 1;\ny = 2;\n", ("x = 1;", "y = 9;"), ("y = 2;", "z = 8;"));

        Assert.Equal("y = 9;\nz = 8;\n", NewTextOf(plan));
    }

    [Fact]
    public void EmptyNewString_DeletesTheMatch()
    {
        FileEditPlan plan = Plan("a\nremove me\nc\n", ("remove me\n", ""));

        Assert.Equal("a\nc\n", NewTextOf(plan));
    }

    [Fact]
    public void AmbiguousOldString_FailsWithTheOccurrenceCount()
    {
        FileEditPlan plan = Plan("dup\ndup\n", ("dup", "x"));

        Assert.False(plan.Succeeded);
        Assert.Contains("occurs 2 times", plan.Error);
        Assert.Contains("edits[0]", plan.Error);
    }

    [Fact]
    public void MissingOldString_FailsAndNamesTheEntry()
    {
        FileEditPlan plan = Plan("a\nb\n", ("a", "A"), ("nowhere", "x"));

        Assert.False(plan.Succeeded);
        Assert.Contains("edits[1]", plan.Error);
        Assert.Contains("was not found", plan.Error);
    }

    [Fact]
    public void EmptyOldString_Fails()
    {
        FileEditPlan plan = Plan("a\n", ("", "x"));

        Assert.False(plan.Succeeded);
        Assert.Contains("must not be empty", plan.Error);
    }

    [Fact]
    public void OverlappingEdits_FailAndNameBothEntries()
    {
        FileEditPlan plan = Plan("abcdef\n", ("abcd", "X"), ("cdef", "Y"));

        Assert.False(plan.Succeeded);
        Assert.Contains("overlap", plan.Error);
        Assert.Contains("edits[0]", plan.Error);
        Assert.Contains("edits[1]", plan.Error);
    }

    [Fact]
    public void NoOpEdit_Fails()
    {
        FileEditPlan plan = Plan("same\n", ("same", "same"));

        Assert.False(plan.Succeeded);
        Assert.Contains("No change", plan.Error);
    }

    [Fact]
    public void NoEdits_Fails()
    {
        Assert.False(FileEditPlanner.Plan("a\n", "x.cs", []).Succeeded);
        Assert.False(FileEditPlanner.Plan("a\n", "x.cs", null).Succeeded);
    }

    // ---- 保守 fuzzy ----

    /// <summary>行尾多余空白不该让模型白跑一轮：它复制粘贴时经常把行尾空白吃掉或加上</summary>
    [Fact]
    public void TrailingWhitespaceDifference_IsAbsorbed()
    {
        FileEditPlan plan = Plan("void F()   \n{\n}\n", ("void F()\n{\n", "void G()\n{\n"));

        Assert.Equal("void G()\n{\n}\n", NewTextOf(plan));
    }

    /// <summary>模型一律按 \n 写，文件却可能是 CRLF。切行时两种终止符都不参与比较，差异结构性地被吸收</summary>
    [Fact]
    public void LfOldString_MatchesCrlfFile()
    {
        FileEditPlan plan = Plan("a\r\nold\r\nc\r\n", ("a\nold\n", "a\nnew\n"));

        Assert.Equal("a\r\nnew\r\nc\r\n", NewTextOf(plan));
    }

    /// <summary>新插入的行按文件的行尾风格写，不是按模型给的 \n</summary>
    [Fact]
    public void InsertedLines_TakeTheFilesLineEnding()
    {
        FileEditPlan plan = Plan("a\r\nb\r\n", ("b", "b1\nb2"));

        Assert.Equal("a\r\nb1\r\nb2\r\n", NewTextOf(plan));
    }

    /// <summary>
    /// pi 的 fuzzy 头一步是 NFKC，会把 （）：， 映射成 ASCII 半角。
    /// 本仓注释通篇中文全角标点，那一步必须不存在——这条测试就是那个决定的看门人
    /// </summary>
    [Fact]
    public void FullWidthPunctuation_IsNeverRewritten()
    {
        const string text = "// 和服务器通信（注意：这里会阻塞）\nvoid Send()  \n{\n}\n";

        FileEditPlan plan = Plan(text, ("void Send()\n{\n", "void Send()\n{\n    Log();\n"));

        string result = NewTextOf(plan);
        Assert.Contains("（注意：这里会阻塞）", result);
        Assert.DoesNotContain("(注意:", result);
    }

    /// <summary>混用换行的文件不该被统一：未命中的行连字节都不该动</summary>
    [Fact]
    public void MixedLineEndings_UntouchedLinesKeepTheirBytes()
    {
        FileEditPlan plan = Plan("crlf\r\nlf\ntarget\r\n", ("target", "hit"));

        Assert.Equal("crlf\r\nlf\nhit\r\n", NewTextOf(plan));
    }

    /// <summary>行内片段靠精确匹配，fuzzy 不该把"整行相等"放宽成"某行含有"</summary>
    [Fact]
    public void PartialLine_StillMatchesExactly()
    {
        FileEditPlan plan = Plan("int total = a + b;\n", ("a + b", "b + a"));

        Assert.Equal("int total = b + a;\n", NewTextOf(plan));
    }

    // ---- diff ----

    [Fact]
    public void Diff_CarriesLineNumbersAndContext()
    {
        FileEditPlan plan = Plan("l1\nl2\nl3\nold\nl5\nl6\n", ("old", "new"));

        Assert.True(plan.Succeeded, plan.Error);
        LineDiffEntry removed = Assert.Single(plan.Diff, x => x.Kind == ELineDiffKind.Removed);
        LineDiffEntry added = Assert.Single(plan.Diff, x => x.Kind == ELineDiffKind.Added);
        Assert.Equal(4, removed.LineNumber);
        Assert.Equal(4, added.LineNumber);
        Assert.Equal(2, plan.Diff.Count(x => x.Kind == ELineDiffKind.Context && x.LineNumber < 4)); //前置上下文
        Assert.Equal(2, plan.Diff.Count(x => x.Kind == ELineDiffKind.Context && x.LineNumber > 4)); //后置上下文
    }

    /// <summary>两处改动挨得近时并成一块：中间的 b/c 各出现一次，而不是被两块的上下文各带一遍</summary>
    [Fact]
    public void Diff_MergesNearbyHunks()
    {
        FileEditPlan plan = Plan("a\nb\nc\nd\n", ("a", "A"), ("d", "D"));

        Assert.True(plan.Succeeded, plan.Error);
        Assert.Equal([2, 3],
            plan.Diff.Where(x => x.Kind == ELineDiffKind.Context).Select(x => x.LineNumber).ToArray());
    }

    [Fact]
    public void RenderDiff_CapsAndCountsTheRest()
    {
        List<LineDiffEntry> diff = Enumerable.Range(1, 10)
            .Select(i => new LineDiffEntry(ELineDiffKind.Added, $"line{i}", i)).ToList();

        string rendered = FileEditPlanner.RenderDiff(diff, 4);

        Assert.Equal(5, rendered.Split('\n').Length); //4 行 + 折叠提示
        Assert.Contains("+6 more diff lines", rendered);
        Assert.StartsWith("+ 1 line1", rendered); //行号按最宽的那个右对齐
    }

    // ---- 落盘保真 ----

    [Fact]
    public async Task Bom_SurvivesAnEdit_AndIsNeverAddedToFilesWithoutIt()
    {
        string dir = Directory.CreateTempSubdirectory("uiharu-bom-").FullName;
        try
        {
            string withBom = Path.Combine(dir, "bom.cs");
            string withoutBom = Path.Combine(dir, "plain.cs");
            await File.WriteAllTextAsync(withBom, "old\n", new UTF8Encoding(true));
            await File.WriteAllTextAsync(withoutBom, "old\n", new UTF8Encoding(false));

            foreach (string path in new[] { withBom, withoutBom })
            {
                FileEditPlan plan = await FileEditPlanner.PlanFileAsync(path, "f.cs",
                    [new FileEdit { OldString = "old", NewString = "new" }]);
                Assert.True(plan.Succeeded, plan.Error);
                await File.WriteAllBytesAsync(path, plan.Envelope.ToBytes(plan.NewText));
            }

            Assert.Equal([0xEF, 0xBB, 0xBF], (await File.ReadAllBytesAsync(withBom))[..3]);
            Assert.Equal("new\n", await File.ReadAllTextAsync(withBom));
            Assert.Equal("new\n"u8.ToArray(), await File.ReadAllBytesAsync(withoutBom));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task MissingFile_FailsWithTheLabelTheModelUsed()
    {
        FileEditPlan plan = await FileEditPlanner.PlanFileAsync(
            Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.cs"), "src/Nope.cs",
            [new FileEdit { OldString = "a", NewString = "b" }]);

        Assert.False(plan.Succeeded);
        Assert.Contains("'src/Nope.cs' not found", plan.Error);
    }
}
