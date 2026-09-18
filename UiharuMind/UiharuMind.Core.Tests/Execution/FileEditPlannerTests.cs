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

    // ---- NotFound 候选提示 ----

    /// <summary>
    /// oldString 找不到时,如果它跟文件里某一行足够像,话术里要带"第几行附近"的候选,
    /// 让模型一步修正而不是反复读抄
    /// </summary>
    [Fact]
    public void MissingOldString_GivesClosestLineHint()
    {
        FileEditPlan plan = Plan("public void Send()  \n{\n}\n", ("public void SendMsg()\n{", "public void SendMsg()\n{\n    Log();\n}"));

        Assert.False(plan.Succeeded);
        Assert.Contains("was not found", plan.Error);
        Assert.Contains("line 1", plan.Error);
        Assert.Contains("public void Send()", plan.Error);
    }

    /// <summary>
    /// oldString 与文件里任何一行都不够像时<b>不</b>给候选——避免把"没找到"误导向"第 N 行"
    /// 让模型以为差一点点,实际差了十万八千里
    /// </summary>
    [Fact]
    public void MissingOldString_FarFromEveryLine_NoHint()
    {
        FileEditPlan plan = Plan("a\n完全无关的一行\nc\n", ("zzzzz_qwerty_alpha\n{", "x"));

        Assert.False(plan.Succeeded);
        Assert.Contains("was not found", plan.Error);
        Assert.DoesNotContain("Closest match", plan.Error);
    }

    /// <summary>
    /// 整块只差缩进是旧版最常翻车的场景:单行评分会指向远处一句内容相近的注释。
    /// 块级候选必须把内容同、空白差的行标成 [ws],并写清首空白数差。
    /// </summary>
    [Fact]
    public void MissingOldString_WhitespaceOnlyDiff_NamesLineAndWhitespace()
    {
        FileEditPlan plan = Plan(
            "/// <summary>搜索失败的种类</summary>\n"
            + "public enum ESearchFailureKind\n"
            + "{\n"
            + "    /// <summary>搜索根目录不存在</summary>\n"
            + "    DirectoryNotFound,\n",
            ("    /// <summary>搜索失败的种类</summary>\n    public enum ESearchFailureKind\n    {\n"
             + "        /// <summary>搜索根目录不存在</summary>\n        DirectoryNotFound,", "x"));

        Assert.False(plan.Succeeded);
        Assert.Contains("Closest match: line 1 (5/5 lines content-matched)", plan.Error);
        Assert.Contains("[ws]", plan.Error);
        Assert.Contains("line 1: expected", plan.Error);
        Assert.Contains("leading whitespace 4 vs 0", plan.Error);
    }

    /// <summary>
    /// 块里第二行内容不同时,话术要指名是第几行,而不是只报一个笼统的"最近行"。
    /// </summary>
    [Fact]
    public void MissingOldString_SecondLineDiffers_NamesThatLine()
    {
        FileEditPlan plan = Plan("first\nsecond\nthird\n",
            ("first\nwrong\nthird", "x"));

        Assert.False(plan.Succeeded);
        Assert.Contains("Closest match: line 1 (2/3 lines content-matched)", plan.Error);
        Assert.Contains("line 2: expected 'wrong' but found 'second'", plan.Error);
        Assert.Contains("content differs", plan.Error);
    }

    /// <summary>
    /// 尾随换行的 oldString（要连换行一起换）失败时也要给块级候选——
    /// 不能因为末尾空段直接放弃（镜像 LocateByLineWindow 的 consumesTerminator）。
    /// </summary>
    [Fact]
    public void MissingOldString_TrailingNewlineBlock_GivesBlockHint()
    {
        FileEditPlan plan = Plan("first\nsecond\nthird\n", ("first\nsecon\n", "x"));

        Assert.False(plan.Succeeded);
        Assert.Contains("Closest match: line 1 (1/2 lines content-matched)", plan.Error);
        Assert.Contains("line 2: expected 'secon' but found 'second'", plan.Error);
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

    /// <summary>模型把中文注释里的全角标点抄成半角（或反之）是常见错误，白名单归一要救回这类失败</summary>
    [Fact]
    public void FullWidthOldString_MatchesHalfWidthFileLine()
    {
        // 文件是半角注释，模型把 oldString 写成全角
        FileEditPlan plan = Plan("// 注意:这里会阻塞\nvoid Send()\n{\n}\n",
            ("// 注意：这里会阻塞\nvoid Send()\n{\n", "void Send()\n{\n    Log();\n"));

        Assert.True(plan.Succeeded, plan.Error);
        Assert.Equal("void Send()\n{\n    Log();\n}\n", NewTextOf(plan));
    }

    [Fact]
    public void HalfWidthOldString_MatchesFullWidthFileLine()
    {
        // 文件是全角注释，模型把 oldString 写成半角（更常见：模型从记忆里抄，中文注释偏好全角）
        FileEditPlan plan = Plan("// 注意：这里会阻塞\nvoid Send()\n{\n}\n",
            ("// 注意:这里会阻塞\nvoid Send()\n{\n", "void Send()\n{\n    Log();\n"));

        Assert.True(plan.Succeeded, plan.Error);
        Assert.Equal("void Send()\n{\n    Log();\n}\n", NewTextOf(plan));
    }

    /// <summary>
    /// 白名单边界：智能引号/破折号没有无语义冲突的半角对应，不归一。
    /// “→" 会把注释里的引号与代码字符串混为一谈，那一步不能存在。
    /// </summary>
    [Fact]
    public void QuotationMarks_AreNotNormalized()
    {
        FileEditPlan plan = Plan("// \"quoted\"\nx\n", ("// “quoted”\nx\n", "y\n"));

        Assert.False(plan.Succeeded);
        Assert.Contains("was not found", plan.Error);
    }

    [Fact]
    public void EmDash_IsNotNormalized()
    {
        FileEditPlan plan = Plan("// a -- b\nx\n", ("// a — b\nx\n", "y\n"));

        Assert.False(plan.Succeeded);
        Assert.Contains("was not found", plan.Error);
    }

    /// <summary>
    /// 文件同时存在全角与半角变体行时,归一会让两个窗口都命中——必须点破"差异只在标点",
    /// 否则模型以为真有俩一模一样的块、去加上下文永远加不对。
    /// 构造:文件里两处都是全角冒号,模型 oldString 抄成半角 → 精确 0 命中、归一两处命中。
    /// </summary>
    [Fact]
    public void FullWidthAmbiguity_NotUnique_NamesPunctuationAsTheDifference()
    {
        FileEditPlan plan = Plan(
            "// 注意：会阻塞\nvoid Send()\n{\n}\n// 注意：会阻塞\nvoid Send()\n{\n}\n",
            ("// 注意:会阻塞\nvoid Send()\n{\n", "x\n"));

        Assert.False(plan.Succeeded);
        Assert.Contains("occurs 2 times", plan.Error);
        Assert.Contains("differ only in full-width punctuation", plan.Error);
    }

    /// <summary>普通的多处命中（无全角参与）保持原话术，不误导模型</summary>
    [Fact]
    public void PlainAmbiguity_NotUnique_KeepsOriginalMessage()
    {
        FileEditPlan plan = Plan("same\nsame\n", ("same", "x"));

        Assert.False(plan.Succeeded);
        Assert.Contains("occurs 2 times", plan.Error);
        Assert.DoesNotContain("full-width", plan.Error);
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

    /// <summary>
    /// 块头必须声明旧/新两套坐标，否则模型把 + 行（新坐标）和 - 行（旧坐标）当同一个坐标系，
    /// 拿行号去文件里找就找错地方。这是之前实机踩过的问题，这里钉死。
    /// </summary>
    [Fact]
    public void Diff_EmitsHunkHeader_DeclaringBothCoordinates()
    {
        // 单块：改 "old"（旧起行 4，旧 5 行）→ "new"（新起行 4，新 5 行）
        FileEditPlan plan = Plan("l1\nl2\nl3\nold\nl5\nl6\n", ("old", "new"));

        Assert.True(plan.Succeeded, plan.Error);
        LineDiffEntry hunk = Assert.Single(plan.Diff, x => x.Kind == ELineDiffKind.Hunk);
        Assert.Equal("@@ -2,5 +2,5 @@", hunk.Text);

        // 渲染时 hunk 行整体输出，不带 + - 前缀或行号列
        string rendered = FileEditPlanner.RenderDiff(plan.Diff, 100);
        Assert.Contains("@@ -2,5 +2,5 @@", rendered);
        Assert.DoesNotContain("+ 2 2", rendered);
    }

    /// <summary>
    /// 多块时第二块的新起行必须带上前面块的行数偏移（delta），
    /// 否则模型以为第二块改动落在旧文件的同一位置，行号错位。
    /// </summary>
    [Fact]
    public void Diff_TwoHunks_SecondHeaderShiftsNewLineByDelta()
    {
        // 第一块把 1 行 "old1" 换成 2 行（新起同旧起 2，新 6 行）；
        // 第二块在下面：旧起 7，但新文件里它已被前一块推后 1 行 → 新起 8。
        FileEditPlan plan = Plan("l1\nl2\nl3\nold1\nl5\nl6\nl7\nl8\nold2\nl10\n",
            ("old1", "new1\nnew1b"), ("old2", "x"));

        Assert.True(plan.Succeeded, plan.Error);
        string[] hunkTexts = plan.Diff.Where(x => x.Kind == ELineDiffKind.Hunk)
            .Select(x => x.Text).ToArray();
        Assert.Equal(["@@ -2,5 +2,6 @@", "@@ -7,4 +8,4 @@"], hunkTexts);
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

    /// <summary>
    /// 截断预算不够时,后一块<b>保留 hunk 头但内容放弃</b>——模型至少知道"这里还有一块"
    /// 及其坐标,不会拿到"有身无头"或"有头无身"。折叠提示带块数与 Read 指引。
    /// </summary>
    /// <summary>整文件被一个块删光时,新侧是 0 行,git 惯例是 <c>+0,0</c> 而不是 <c>+1,0</c></summary>
    [Fact]
    public void Diff_EntireFileDeleted_UsesZeroZeroForNewSide()
    {
        FileEditPlan plan = Plan("l1\nl2\nold\nl4\n", ("l1\nl2\nold\nl4\n", ""));

        Assert.True(plan.Succeeded, plan.Error);
        LineDiffEntry hunk = Assert.Single(plan.Diff, x => x.Kind == ELineDiffKind.Hunk);
        Assert.EndsWith(" +0,0 @@", hunk.Text);
    }

    [Fact]
    public void RenderDiff_Truncation_KeepsHunkHeadersWhole()
    {
        List<LineDiffEntry> diff =
        [
            new(ELineDiffKind.Hunk, "@@ -1,5 +1,5 @@"),
            new(ELineDiffKind.Context, "c1", 1),
            new(ELineDiffKind.Removed, "old", 2),
            new(ELineDiffKind.Added, "new", 2),
            new(ELineDiffKind.Hunk, "@@ -10,3 +10,3 @@"),
            new(ELineDiffKind.Context, "c2", 10),
            new(ELineDiffKind.Removed, "old2", 11),
            new(ELineDiffKind.Added, "new2", 11),
        ];

        // 预算 5:第一块 4 行放下,第二块 4 行放不下 → 保留头、内容放弃
        string rendered = FileEditPlanner.RenderDiff(diff, 5);
        Assert.Contains("@@ -1,5 +1,5 @@", rendered);
        Assert.Contains("@@ -10,3 +10,3 @@", rendered); //第二块头保留,说明"这里还有一块"
        Assert.DoesNotContain("old2", rendered); //第二块内容放弃,不出现半截
        Assert.Contains("+3 more diff lines across 1 hunk(s)", rendered); //内容 3 行 + 块数
    }

    /// <summary>
    /// 单块超大（内容行数超过预算）时,不能只给头不给内容——否则模型对大编辑完全无感、
    /// 自纠能力归零。必须从头给尽可能多的内容行,并提示剩余行数与 Read 定位。
    /// </summary>
    [Fact]
    public void RenderDiff_Truncation_LargeSingleHunk_ShowsHeadContent()
    {
        List<LineDiffEntry> diff =
        [
            new(ELineDiffKind.Hunk, "@@ -1,10 +1,10 @@"),
            new(ELineDiffKind.Context, "c1", 1),
            new(ELineDiffKind.Removed, "old2", 2),
            new(ELineDiffKind.Added, "new2", 2),
            new(ELineDiffKind.Context, "c3", 3),
            new(ELineDiffKind.Context, "c4", 4),
            new(ELineDiffKind.Context, "c5", 5),
            new(ELineDiffKind.Context, "c6", 6),
            new(ELineDiffKind.Context, "c7", 7),
            new(ELineDiffKind.Context, "c8", 8),
        ];

        // 预算 5:头 + 4 行内容,剩余 5 行折叠(提示带 Read 指引)
        string rendered = FileEditPlanner.RenderDiff(diff, 5);
        Assert.Contains("@@ -1,10 +1,10 @@", rendered);
        Assert.Contains("old2", rendered); //头几行内容要给到
        Assert.Contains("new2", rendered);
        Assert.DoesNotContain("c7", rendered); //超出预算的行不给
        Assert.Contains("+5 more diff lines across 1 hunk(s); use Read offset=… to inspect", rendered);
    }

    [Fact]
    public void RenderDiff_Truncation_WithoutHunks_FallsBackToLineTruncation()
    {
        // 无 hunk 头(非本工具产物):保持旧的逐行截断行为
        List<LineDiffEntry> diff = Enumerable.Range(1, 6)
            .Select(i => new LineDiffEntry(ELineDiffKind.Added, $"line{i}", i)).ToList();

        string rendered = FileEditPlanner.RenderDiff(diff, 3);
        Assert.Equal(4, rendered.Split('\n').Length); //3 行 + 折叠提示
        Assert.Contains("+3 more diff lines", rendered);
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
