using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// Read 的附注：截断给总行数、负数 offset 读末尾、空文件与越界分开说、
/// 传目录列一层子项（目录优先占额度，额度不够时如实报账）、二进制不硬读、找不到时列出工作区里的同名文件
/// </summary>
public sealed class ReadToolTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uiharu-read-").FullName;
    private readonly PermissiveFileAccessTools _tools;

    public ReadToolTests()
    {
        _tools = new PermissiveFileAccessTools(_dir);
    }

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

    private string Save(string relative, string text) => Save(relative, System.Text.Encoding.UTF8.GetBytes(text));

    private string Save(string relative, byte[] bytes)
    {
        string path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private Task<string> Read(string path, int offset = 1, int? limit = null) =>
        _tools.Read(path, offset, limit, cancellationToken: TestContext.Current.CancellationToken);

    private static string Lines(int count) => string.Join('\n', Enumerable.Range(1, count).Select(i => $"line{i}")) + "\n";

    /// <summary>limit 只表示行数：负数不当成「末尾」或「全文」，退回并指明两种正确写法</summary>
    [Fact]
    public async Task NonPositiveLimit_IsRejectedWithTheRightWays()
    {
        string path = Save("small.txt", Lines(5));

        string result = await Read(path, limit: -1);

        Assert.Equal("limit 须为正数（所传 -1）；读末尾几行用负的 offset，读全文传 whole=true。", result);
        Assert.Equal("5\tline5\n[已到文件末尾，共 5 行]", await Read(path, offset: -1));
    }

    [Fact]
    public async Task TruncationNotice_GivesTheTotalLineCount()
    {
        string path = Save("big.txt", Lines(2500));

        string result = await Read(path, limit: 100);

        Assert.EndsWith("…[已截断：显示第 1–100 行，共 2500 行；续读传 offset=101]", result);
        Assert.DoesNotContain("Summary", result); //Summary 没挂就不提
    }

    /// <summary>Summary 挂着、且是工具自己截断时，截断附注后指一条去处：整体理解交给它，不一页页翻</summary>
    [Fact]
    public async Task TruncationNotice_PointsToSummary_WhenMounted()
    {
        string path = Save("big.txt", Lines(2500));
        var tools = new PermissiveFileAccessTools(new AgentPathResolver(_dir), offerSummary: true);
        CancellationToken ct = TestContext.Current.CancellationToken;

        string result = await tools.Read(path, 1, null, cancellationToken: ct);

        Assert.EndsWith($"]\n要整体理解这份文件，可交给 `{BrainTool.ToolName}`，task 里写清要什么。", result);
        Assert.DoesNotContain(BrainTool.ToolName, await tools.Read(path, 1, 100, cancellationToken: ct)); //自己传了 limit 是在按段读，不提
        Assert.DoesNotContain(BrainTool.ToolName, await tools.Read(Save("small.txt", Lines(3)), 1, null,
            cancellationToken: ct)); //没截断不提
    }

    /// <summary>全文窗按结果推首行行号：截断附注为准（负 offset 也对），没截断用正 offset</summary>
    [Fact]
    public async Task FirstLineOf_MatchesTheFileLineNumbers()
    {
        string path = Save("big.txt", Lines(200));

        Assert.Equal(60, ReadTruncationNote.FirstLineOf(60, await Read(path, offset: 60, limit: 40)));
        Assert.Equal(151, ReadTruncationNote.FirstLineOf(-50, await Read(path, offset: -50, limit: 10)));
        Assert.Equal(190, ReadTruncationNote.FirstLineOf(190, await Read(path, offset: 190)));
        Assert.Equal(1, ReadTruncationNote.FirstLineOf(0, await Read(path, offset: 0, limit: 5)));
    }

    [Fact]
    public async Task NegativeOffset_ReadsTheTail()
    {
        string path = Save("log.txt", Lines(10));

        Assert.Equal("8\tline8\n9\tline9\n10\tline10\n[已到文件末尾，共 10 行]", await Read(path, offset: -3));
        Assert.StartsWith("1\tline1\n", await Read(path, offset: -50)); //倒数超过总行数就从头读
    }

    [Fact]
    public async Task OffsetPastTheEnd_SaysHowLongTheFileIs()
    {
        string path = Save("short.txt", Lines(10));

        Assert.Equal($"offset 20 超出了 '{path}' 的末尾（共 10 行）。", await Read(path, offset: 20));
    }

    [Fact]
    public async Task EmptyFile_IsReportedAsEmpty()
    {
        string path = Save("empty.txt", string.Empty);

        Assert.Equal($"文件 '{path}' 是空的。", await Read(path));
    }

    [Fact]
    public async Task CrLfFile_IsShownWithPlainNewLines()
    {
        string path = Save("crlf.txt", "a\r\nb\r\nc");

        Assert.Equal("1\ta\n2\tb\n3\tc\n[已到文件末尾，共 3 行]", await Read(path));
    }

    /// <summary>
    /// 传目录 = 列它这一层：文件在前带大小、目录在后带斜杠，且先声明这是目录列表。
    /// 脑（ADR 0082）只挂 Read/Grep，这是它唯一的目录入口；直接枚举，不套 Glob 排除
    /// </summary>
    [Fact]
    public async Task Directory_ListsOnlyItsDirectChildren()
    {
        Save("src/Program.cs", "code");
        Save("src/Inner/Deep.cs", "code");

        string result = await Read("src");

        Assert.Contains("目录 \"src\" 的直接子项", result);
        Assert.Contains("src/Program.cs (4 B)", result);
        Assert.Contains("src/Inner/", result);
        Assert.DoesNotContain("Deep.cs", result); //子目录里的文件不跟着铺开
        // 文件在前、目录在后：模型先看见可读的文件，再看见可下的子目录
        Assert.True(result.IndexOf("src/Program.cs", StringComparison.Ordinal)
                    < result.IndexOf("src/Inner/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmptyDirectory_SaysSo()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "empty"));

        string result = await Read("empty");

        Assert.Contains("是空的", result);
    }

    /// <summary>列目录不套 Glob 的硬排除/ignore：bin、点文件这些“搜索时跳过”的仍然如实列出</summary>
    [Fact]
    public async Task Directory_ListsEntriesThatSearchWouldSkip()
    {
        Save("src/bin/tool.dll", [1, 2, 3, 4]);
        Save("src/.env", "x");

        string result = await Read("src");

        Assert.Contains("src/bin/", result);
        Assert.Contains("src/.env (1 B)", result);
    }

    /// <summary>带上一堆读文件的参数也不报错，只补一句它们没生效；limit=0 同理会先拿到列表</summary>
    [Fact]
    public async Task Directory_IgnoresFileReadingParameters()
    {
        Save("src/a.txt", "x");

        Assert.EndsWith("\noffset/limit/whole/outline 是读文件的参数，列目录时已忽略。", await Read("src", offset: 5, limit: 2));
        Assert.Contains("已忽略", await Read("src", limit: 0));
    }

    /// <summary>
    /// 过宽的目录不铺半截也不数全量：多取一个条目就知道超了，给出收窄去处。
    /// 没有子目录时不得提「额度留给子目录」——那会描出一个根本不存在的层
    /// </summary>
    [Fact]
    public async Task WideDirectory_ListsTheFirstEntriesAndSaysThereAreMore()
    {
        const int extra = 10;
        for (int i = 0; i < PermissiveFileAccessTools.MaxGlobEntries + extra; i++) Save($"many/f{i:D3}.txt", "x");

        string result = await Read("many");

        Assert.Contains($"文件只给出 {PermissiveFileAccessTools.MaxGlobEntries} 个", result);
        Assert.Contains("用 Grep 按内容定位", result);
        Assert.DoesNotContain("额度优先留给了子目录", result);
    }

    /// <summary>
    /// 子目录名排在文件后面、文件又多到超过额度时，子目录仍要被列出：额度先给子目录。
    /// 枚举按名字走，160 个 a* 文件 + 一个 zdir，从前收满 150 条就停，前 150 条里 0 个目录——
    /// 而 Read 是脑唯一的目录入口，看不见子目录就无路可走
    /// </summary>
    [Fact]
    public async Task Subdirectory_IsNotCutWhenFilesFillTheQuota()
    {
        const int extra = 10;
        for (int i = 0; i < PermissiveFileAccessTools.MaxGlobEntries + extra; i++) Save($"wide/a{i:D3}.txt", "x");
        Directory.CreateDirectory(Path.Combine(_dir, "wide", "zdir"));

        string result = await Read("wide");

        Assert.Contains("wide/zdir/", result);
        Assert.Contains("文件只给出 149 个", result); //文件被裁，如实报账
    }

    /// <summary>子目录自己就占满额度：如实说文件没列（文件排在前，确实被看到了）</summary>
    [Fact]
    public async Task SubdirectoriesFillingTheQuota_SayFilesAreNotListed()
    {
        for (int i = 0; i <= PermissiveFileAccessTools.MaxGlobEntries; i++)
            Directory.CreateDirectory(Path.Combine(_dir, "all", $"d{i:D3}"));
        Save("all/aaa.txt", "x");

        string result = await Read("all");

        Assert.Contains($"子目录超过 {PermissiveFileAccessTools.MaxGlobEntries} 个", result);
        Assert.Contains("文件未列出", result);
        Assert.DoesNotContain("是空的", result);
    }

    /// <summary>
    /// 子目录恰好占满额度且目录里<b>没有文件</b>：一个字也不提文件——
    /// 「额度不够」与「根本没有」是两回事，后者说了就是骗模型去找不存在的东西
    /// </summary>
    [Fact]
    public async Task DirectoriesFillingTheQuota_WithoutFiles_GiveNoFileNotice()
    {
        for (int i = 0; i < PermissiveFileAccessTools.MaxGlobEntries; i++)
            Directory.CreateDirectory(Path.Combine(_dir, "onlydirs", $"d{i:D3}"));

        string result = await Read("onlydirs");

        Assert.Contains("onlydirs/d000/", result);
        Assert.DoesNotContain("文件未列出", result);
        Assert.DoesNotContain("文件只给出", result);
        Assert.DoesNotContain("子目录超过", result);
    }

    /// <summary>
    /// 子目录恰好等于上限、后面只剩文件：不误报“子目录还有更多”。
    /// 额度判定在 include 过滤之后——否则只收目录的那一遍会被后面的文件顶成误报
    /// </summary>
    [Fact]
    public async Task DirectoriesExactlyAtTheLimit_DoNotClaimMoreDirectories()
    {
        for (int i = 0; i < PermissiveFileAccessTools.MaxGlobEntries; i++)
            Directory.CreateDirectory(Path.Combine(_dir, "edge", $"d{i:D3}"));
        Save("edge/zz.txt", "x");

        string result = await Read("edge");

        Assert.DoesNotContain("子目录超过", result);
        Assert.Contains("文件未列出", result);
    }

    /// <summary>
    /// 记忆目录本身读的仍是索引而不是列表：TryGetListableDirectory 里那道唯一的护栏。
    /// Read 是工具层的唯一入口，护栏一挪，模型就只看得见“有什么文件”而看不见摘要
    /// </summary>
    [Fact]
    public async Task MemoryDirectory_StillReadsTheIndexInsteadOfAListing()
    {
        string memory = Path.Combine(_dir, "memory");
        Save("memory/note.md", "- 第一条笔记");
        PermissiveFileAccessTools tools = new(new AgentPathResolver(_dir, memoryRoot: memory));

        string result = await tools.Read("$MEMORY", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("note.md", result);
        Assert.DoesNotContain("的直接子项", result);
    }

    /// <summary>条目数正好等于上限时不说“只列了前 N 项”——那就是全部</summary>
    [Fact]
    public async Task DirectoryExactlyAtTheLimit_IsNotCalledTruncated()
    {
        for (int i = 0; i < PermissiveFileAccessTools.MaxGlobEntries; i++) Save($"exact/f{i:D3}.txt", "x");

        string result = await Read("exact");

        Assert.DoesNotContain("只列了前", result);
    }

    /// <summary>工作区根也能 Read “.”：相对输出、不炸</summary>
    [Fact]
    public async Task WorkspaceRoot_CanBeListed()
    {
        Save("root.txt", "x");

        string result = await Read(".");

        Assert.Contains("root.txt (1 B)", result);
    }

    /// <summary>同类内部按路径 Ordinal 升序：模型扫得出一组文件的命名规律</summary>
    [Fact]
    public async Task Entries_WithinTheSameKind_AreSortedOrdinally()
    {
        Save("ord/b.txt", "x");
        Save("ord/A.txt", "x");
        Directory.CreateDirectory(Path.Combine(_dir, "ord", "Zdir"));
        Directory.CreateDirectory(Path.Combine(_dir, "ord", "adir"));

        string result = await Read("ord");

        Assert.True(result.IndexOf("ord/A.txt", StringComparison.Ordinal)
                    < result.IndexOf("ord/b.txt", StringComparison.Ordinal));
        Assert.True(result.IndexOf("ord/Zdir/", StringComparison.Ordinal)
                    < result.IndexOf("ord/adir/", StringComparison.Ordinal));
    }

    /// <summary>工作区外的绝对路径也能列：回显与条目都走绝对路径（与 Glob 同口径）</summary>
    [Fact]
    public async Task OutsideTheWorkspace_AbsoluteDirectoryIsListed()
    {
        string outside = Directory.CreateTempSubdirectory("uiharu-outside-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(outside, "note.txt"), "x");

            string result = await Read(outside);

            Assert.Contains("note.txt", result);
            Assert.Contains(outside, result); //回显是绝对路径，不硬凑 ../
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    /// <summary>记忆目录的<b>子目录</b>照常列：读索引只针对记忆根，别无脑扩大到整棵记忆树</summary>
    [Fact]
    public async Task MemorySubdirectory_IsListedLikeAnyOtherDirectory()
    {
        string memory = Path.Combine(_dir, "memory");
        Save("memory/sub/note.md", "- 笔记");
        PermissiveFileAccessTools tools = new(new AgentPathResolver(_dir, memoryRoot: memory));

        string result = await tools.Read("$MEMORY/sub", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("的直接子项", result);
        Assert.Contains("note.md", result);
    }

    /// <summary>绕过工具面直接读目录时的兜底：说清不能当文件读，不再指 Glob</summary>
    [Fact]
    public async Task FileReader_OnADirectory_SaysItIsNotAFile()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "adir"));
        FileReader reader = new(new AgentPathResolver(_dir), EReadImageMode.None, null);

        string result = await reader.ReadAsync("adir", 1, null, TestContext.Current.CancellationToken);

        Assert.Contains("是目录", result);
        Assert.Contains("不能当文件读", result);
    }

    /// <summary>读一次二进制从前就是 32KB 乱码进上下文</summary>
    [Fact]
    public async Task BinaryFile_IsNotDumpedAsText()
    {
        Save("lib.dll", [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0xFF, 0xFE]);

        string result = await Read("lib.dll");

        Assert.Equal("'lib.dll' 是二进制文件（8 B），无法按文本显示。", result);
    }

    /// <summary>实测找不到的大多是目录猜错了：同名文件就在工作区的别处</summary>
    [Fact]
    public async Task MissingFile_ListsFilesWithTheSameNameElsewhere()
    {
        Save("Code/Social/Public/Laws/Weight.h", "#pragma once\n");

        string result = await Read("Code/Laws/Public/Weight.h");

        Assert.StartsWith("找不到文件 'Code/Laws/Public/Weight.h'。", result);
        Assert.Contains("- Code/Social/Public/Laws/Weight.h", result);
    }

    [Fact]
    public async Task MissingFile_WithoutNamesakes_IsJustNotFound()
    {
        Assert.Equal("找不到文件 'nope.cs'。", await Read("nope.cs"));
    }

    [Fact]
    public async Task WildcardPath_PointsToGlob()
    {
        string result = await Read("issues/162-*.md");

        Assert.Contains($"`{FileToolNames.Glob}`", result);
    }
}
