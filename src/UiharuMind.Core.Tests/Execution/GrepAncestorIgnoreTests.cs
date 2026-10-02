using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 搜子目录时，搜索根<b>之上</b>的忽略规则也要生效。
/// 实机：仓库根写着 <c>/Code/build/</c>，搜 <c>path=Code</c> 九成命中落在构建产物里，正则搜索一次几百 MB 大对象堆分配
/// </summary>
public class GrepAncestorIgnoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uiharu-grep-ignore-").FullName;
    private readonly SimpleGrepper _grepper;

    public GrepAncestorIgnoreTests()
    {
        File.WriteAllText(Path.Combine(_dir, ".gitignore"), "/Code/build/\nIntermediate/\n*.log\n!keep.log\n/Code/src/*.tmp\n");
        Directory.CreateDirectory(Path.Combine(_dir, "Code"));
        File.WriteAllText(Path.Combine(_dir, "Code", ".gitignore"), "gen/\n");
        Write("Code/top.txt");
        Write("Code/src/a.txt");
        Write("Code/src/x.log");
        Write("Code/src/keep.log");
        Write("Code/src/y.tmp");
        Write("Code/src/gen/e.txt");
        Write("Code/lib/l.txt");
        Write("Code/build/b.txt");
        Write("Code/Game/Plugins/X/Intermediate/c.txt");
        Write(".hidden/d.txt");
        _grepper = new SimpleGrepper(_dir);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubdirectorySearch_HonorsAncestorIgnoreRules(bool isRegex)
    {
        GrepOutcome outcome = await _grepper.SearchAsync(isRegex ? "need.e" : "needle", isRegex: isRegex, path: "Code",
            ct: TestContext.Current.CancellationToken);

        Assert.Null(outcome.Failure);
        Assert.Equal(["Code/lib/l.txt", "Code/src/a.txt", "Code/src/keep.log", "Code/top.txt"], Files(outcome));
    }

    /// <summary>取消是真的停下：扫描中途取消，闸随这次调用释放，下一次搜索不用等被丢下的扫描跑完</summary>
    [Fact]
    public async Task CancelledSearch_ReleasesGateForNextSearch()
    {
        for (int i = 0; i < 3000; i++) Write($"Code/bulk/d{i % 30}/f{i}.txt");
        using var cts = new CancellationTokenSource();

        // 进闸是同步完成的，引擎起来之后才回到这里，所以取消落在扫描中途
        Task<GrepOutcome> search = _grepper.SearchAsync("needle", path: "Code", ct: cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => search);
        GrepOutcome outcome = await _grepper.SearchAsync("needle", path: "Code/src", ct: TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(["Code/src/a.txt", "Code/src/keep.log"], Files(outcome));
    }

    /// <summary>上层规则按文件名、带斜杠的通配、否定都与 git 同口径</summary>
    [Fact]
    public async Task FilesIgnoredByAncestorRules_AreDropped()
    {
        GrepOutcome outcome = await _grepper.SearchAsync("needle", path: "Code/src", ct: TestContext.Current.CancellationToken);

        Assert.Equal(["Code/src/a.txt", "Code/src/keep.log"], Files(outcome));
    }

    /// <summary>带上层规则时深度仍按调用方给的搜索根算</summary>
    [Fact]
    public async Task MaxDepth_CountsFromRequestedPathWithAncestorRules()
    {
        GrepOutcome outcome = await _grepper.SearchAsync("needle", maxDepth: 1, path: "Code",
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(["Code/lib/l.txt", "Code/src/a.txt", "Code/src/keep.log", "Code/top.txt"], Files(outcome));
    }

    /// <summary>点名要搜被忽略的目录时照搜——上移引擎根会让引擎把它整个跳过</summary>
    [Fact]
    public async Task ExplicitlyIgnoredDirectory_IsStillSearched()
    {
        GrepOutcome outcome = await _grepper.SearchAsync("needle", path: "Code/build", ct: TestContext.Current.CancellationToken);

        Assert.Equal(["Code/build/b.txt"], Files(outcome));
    }

    /// <summary>途经隐藏目录时不上移：引擎不进隐藏目录</summary>
    [Fact]
    public async Task HiddenDirectorySearch_IsStillSearched()
    {
        GrepOutcome outcome = await _grepper.SearchAsync("needle", path: ".hidden", ct: TestContext.Current.CancellationToken);

        Assert.Equal([".hidden/d.txt"], Files(outcome));
    }

    /// <summary>深度仍按调用方给的搜索根算（引擎只限递归进去的目录层数，0 = 只搜根下的文件）</summary>
    [Fact]
    public async Task MaxDepth_IsRelativeToRequestedPath()
    {
        GrepOutcome outcome = await _grepper.SearchAsync("needle", maxDepth: 0, path: "Code",
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(["Code/top.txt"], Files(outcome));
    }

    private void Write(string relative)
    {
        string path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "a needle here\n");
    }

    private static List<string> Files(GrepOutcome outcome) =>
        outcome.Matches.Select(x => x.FileName.Replace('\\', '/')).Distinct().Order(StringComparer.Ordinal).ToList();
}
