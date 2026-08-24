using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 钉死「搜索失败必须可见」这一组行为。
///
/// 这组测试对应的三个实机症状都是<b>工具骗了模型</b>，而不是模型不会用工具：
/// <list type="number">
/// <item>Grep 的目录不存在时静默返回空列表，与「这个词确实没有」完全无法区分——
/// 模型于是换个关键词在错目录里反复搜，也就是「搜索工具试几次才找到正确用法」。</item>
/// <item>失败以字符串形式混进结果（<c>"[Error] ..."</c> / <c>FileName = ""</c> 的假命中），
/// 模型分不清「这是一处命中」和「这是一句话」。</item>
/// <item><c>*Foo</c> 这类半 glob 半正则的表达式当正则非法、当字面串又搜不到，两条路都不通。</item>
/// </list>
/// </summary>
public class SearchFailureVisibilityTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uiharu-search-").FullName;
    private readonly SimpleGrepper _grepper;
    private readonly SimpleGlobber _globber;

    public SearchFailureVisibilityTests()
    {
        _grepper = new SimpleGrepper(_dir);
        _globber = new SimpleGlobber(_dir);
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

    /// <summary>
    /// 目录不存在<b>不能</b>再表现为「搜到 0 条」。失败原因里必须带上解析后的绝对路径——
    /// 模型看不到自己那个相对路径被拼成了哪里，就只能再猜一次。
    /// </summary>
    [Fact]
    public async Task Grep_DirectoryNotFound_IsAFailure_NotAnEmptyResult()
    {
        GrepOutcome outcome = await _grepper.SearchAsync("anything", directory: "no-such-dir");

        Assert.NotNull(outcome.Failure);
        Assert.Equal(ESearchFailureKind.DirectoryNotFound, outcome.Failure.Kind);
        Assert.Equal("no-such-dir", outcome.Failure.RequestedDirectory);
        Assert.Contains("no-such-dir", outcome.Failure.ResolvedDirectory);
        Assert.Equal(Path.GetFullPath(_dir), outcome.Failure.WorkingDirectory);
        Assert.Empty(outcome.Matches);
    }

    /// <summary>搜到 0 条与没搜成必须是两种不同的返回：前者没有 Failure</summary>
    [Fact]
    public async Task Grep_ZeroHits_IsNotAFailure()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "a.txt"), "hello");

        GrepOutcome outcome = await _grepper.SearchAsync("definitely-not-here");

        Assert.Null(outcome.Failure);
        Assert.Empty(outcome.Matches);
    }

    /// <summary>
    /// <c>*Foo</c>：模型实测最爱写的那一种。归一化成 <c>.*Foo</c> 之后必须一次命中，
    /// 而不是抛正则语法错、也不是 0 命中。
    /// </summary>
    [Fact]
    public async Task Grep_LeadingWildcard_IsNormalisedAndHits()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "a.txt"), "class SimpleGlobber");

        GrepOutcome outcome = await _grepper.SearchAsync("*Globber", isRegex: true);

        Assert.Null(outcome.Failure);
        Assert.NotEmpty(outcome.Matches);
        Assert.Equal(".*Globber", outcome.EffectiveQuery);
        Assert.False(outcome.FellBackToLiteral);
    }

    /// <summary>
    /// 编译不过的正则降级为字面串搜索，<b>并且把降级这件事回报出来</b>：
    /// 不说一声，模型对「为什么少了几条命中」会推错。
    /// </summary>
    [Fact]
    public async Task Grep_InvalidRegex_FallsBackToLiteral_AndSaysSo()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "a.txt"), "await SearchAsync(query);");

        GrepOutcome outcome = await _grepper.SearchAsync("SearchAsync(", isRegex: true);

        Assert.Null(outcome.Failure);
        Assert.NotEmpty(outcome.Matches);
        Assert.True(outcome.FellBackToLiteral);
    }

    /// <summary>归一化只动首尾：中间的 <c>*</c> 是合法正则，动它会改掉一个写对了的表达式</summary>
    [Theory]
    [InlineData("*Foo", ".*Foo")]
    [InlineData("Foo**", "Foo*")]
    [InlineData("Glob.*Search", "Glob.*Search")]
    [InlineData("a*b", "a*b")]
    public void NormalizeRegex_OnlyTouchesUnquantifiableWildcards(string input, string expected)
    {
        Assert.Equal(expected, SimpleGrepper.NormalizeRegex(input));
    }

    /// <summary>
    /// <b>搜索回的路径必须能直接当 Read 的入参</b>——这是"AI 老是用绝对路径"的病根。
    ///
    /// 两个搜索器从前按<b>搜索根</b>算相对路径，而 Read/Edit 按<b>工作区根</b>解析，
    /// 于是 <c>directory</c> 一缩小，回来的路径就喂不回去。模型吃过几次之后倒向绝对路径，
    /// 那是它理性的选择：当时绝对路径是唯一跨工具通用的形式。这条测试就是那个坑的看门人。
    /// </summary>
    [Fact]
    public async Task Grep_NarrowedByDirectory_ReturnsPathsRelativeToWorkspace()
    {
        string sub = Path.Combine(_dir, "sub", "deep");
        Directory.CreateDirectory(sub);
        await File.WriteAllTextAsync(Path.Combine(sub, "hit.txt"), "needle");

        GrepOutcome outcome = await _grepper.SearchAsync("needle", directory: "sub");

        GrepMatchResult match = Assert.Single(outcome.Matches);
        // 相对搜索根会是 "deep/hit.txt",那个路径喂给 Read 会解析到 <工作区>/deep/hit.txt
        Assert.Equal("sub/deep/hit.txt", match.FileName);
        Assert.True(File.Exists(Path.Combine(_dir, match.FileName)),
            "搜索回的路径必须能直接拼在工作区根上打开");
    }

    /// <summary>Glob 同理：缩小 directory 之后回的路径仍相对工作区</summary>
    [Fact]
    public async Task Glob_NarrowedByDirectory_ReturnsPathsRelativeToWorkspace()
    {
        string sub = Path.Combine(_dir, "sub", "deep");
        Directory.CreateDirectory(sub);
        await File.WriteAllTextAsync(Path.Combine(sub, "hit.cs"), "x");

        GlobOutcome outcome = await _globber.SearchAsync("**/*.cs", directory: "sub");

        GlobEntry entry = Assert.Single(outcome.Entries);
        Assert.Equal("sub/deep/hit.cs", entry.Path);
        Assert.False(entry.IsDirectory);
        Assert.Equal(1, entry.SizeBytes); //大小是真取到的,不是占位 0
    }

    /// <summary>Glob 的目录不存在同样是结构化失败，不再是塞进结果列表的一条假条目</summary>
    [Fact]
    public async Task Glob_DirectoryNotFound_IsAFailure_NotAFakeEntry()
    {
        GlobOutcome outcome = await _globber.SearchAsync("**/*.cs", directory: "nope");

        Assert.NotNull(outcome.Failure);
        Assert.Equal(ESearchFailureKind.DirectoryNotFound, outcome.Failure.Kind);
        Assert.Empty(outcome.Entries);
    }

    /// <summary>
    /// Glob 的条目<b>是结构化的，不是渲染好的字符串</b>。
    ///
    /// 它有两个读者：模型那侧要带文件大小的行，界面那侧只要路径去打开文件。
    /// 从前一个字符串走两头、界面靠剥 <c>"[FILE] "</c> 前缀取路径，于是模型那侧一改渲染格式
    /// （比如追加文件大小）界面就静默坏掉——这条测试钉住"两侧各自渲染"。
    /// </summary>
    [Fact]
    public async Task Glob_Entries_CarryStructuredSize_NotRenderedText()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "a.cs"), new string('x', 4096));
        Directory.CreateDirectory(Path.Combine(_dir, "d.cs"));

        GlobOutcome outcome = await _globber.SearchAsync("**/*.cs");

        GlobEntry file = Assert.Single(outcome.Entries, x => !x.IsDirectory);
        Assert.Equal("a.cs", file.Path); //路径里不带任何 [FILE] 前缀或大小后缀
        Assert.Equal(4096, file.SizeBytes);

        GlobEntry dir = Assert.Single(outcome.Entries, x => x.IsDirectory);
        Assert.Equal(0, dir.SizeBytes); //目录不标大小,标了是纯噪声
    }

    /// <summary>
    /// 回给模型的条目<b>文件在前、目录在后</b>。
    ///
    /// 搜索器给的是纯路径字典序，那会让同一层的 [FILE] 与 [DIR] 逐行交替
    /// （README.md / cmake / data / default.nix / docs …），扫起来很费劲。
    /// 文件才是可行动的东西——拿到路径下一步就是 `Read`/`Edit`。
    /// 分组只在工具边界做，搜索器那层保持稳定的路径序。
    /// </summary>
    [Fact]
    public async Task GlobTool_PutsFilesBeforeDirectories()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "cmake"));
        Directory.CreateDirectory(Path.Combine(_dir, "docs"));
        await File.WriteAllTextAsync(Path.Combine(_dir, "README.md"), "x");
        await File.WriteAllTextAsync(Path.Combine(_dir, "default.nix"), "x");

        PermissiveFileAccessTools tools = new(_dir);
        AIFunction glob = tools.Create().OfType<AIFunction>().Single(x => x.Name == FileToolNames.Glob);
        object? raw = await glob.InvokeAsync(new AIFunctionArguments { ["pattern"] = "**/*" });

        // InvokeAsync 回的是已序列化的 JsonElement(工具结果本来就是这样发给模型的),
        // 顺带证明了带 Notice 的包装类型走反射序列化没问题
        JsonElement json = Assert.IsType<JsonElement>(raw);
        List<string> entries = json.GetProperty("entries").EnumerateArray()
            .Select(x => x.GetString() ?? string.Empty).ToList();
        int lastFile = entries.FindLastIndex(x => x.StartsWith("[FILE]", StringComparison.Ordinal));
        int firstDir = entries.FindIndex(x => x.StartsWith("[DIR]", StringComparison.Ordinal));

        Assert.True(lastFile < firstDir,
            $"文件必须全部排在目录之前,实际:{string.Join(" | ", entries)}");
    }

    /// <summary>没有通配符时也是失败，而不是一条以 <c>[Error]</c> 开头的「文件名」</summary>
    [Fact]
    public async Task Glob_PatternWithoutWildcard_IsAFailure()
    {
        GlobOutcome outcome = await _globber.SearchAsync("Program.cs");

        Assert.NotNull(outcome.Failure);
        Assert.Equal(ESearchFailureKind.GlobHasNoWildcard, outcome.Failure.Kind);
        Assert.Empty(outcome.Entries);
    }
}
