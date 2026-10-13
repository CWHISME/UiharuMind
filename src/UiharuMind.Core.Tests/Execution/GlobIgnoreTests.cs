using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉死 <c>SimpleGlobber</c> 的 ignore 规则：与 grep 侧同口径，
/// 搜子目录时搜索根之上的规则也要生效（对标 <c>GrepAncestorIgnoreTests</c>）。
/// 实机：开场 <c>**/CLAUDE.md</c> 这类全树 pattern 在 Unity 工程里会把
/// <c>Library/</c>（上万 artifact 文件）走完，规则生效后整棵剪掉。
/// <c>HardSkips</c> 不动（见 <c>GlobHardSkipTests</c>），这里只锁规则层。
/// </summary>
public class GlobIgnoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uiharu-glob-ignore-").FullName;

    public GlobIgnoreTests()
    {
        File.WriteAllText(Path.Combine(_dir, ".gitignore"), "/Code/build/\nIntermediate/\n*.log\n!keep.log\n/Code/src/*.tmp\nLibrary/\n");
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
        // Unity 布局：Library 整棵忽略，Assets 照收
        Write("Game/Assets/Scripts/Player.cs");
        Write("Game/Assets/Scenes/Main.unity");
        Write("Game/Library/Artifacts/big.bin");
        Write("Game/Library/PackageCache/p.txt");
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

    /// <summary>搜子目录时，搜索根之上的忽略规则生效：目录规则、通配、否定都与 git 同口径</summary>
    [Fact]
    public async Task SubdirectorySearch_HonorsAncestorIgnoreRules()
    {
        // pattern 必须能按后缀命中 .log/.tmp，否则是 pattern 在过滤而不是规则在过滤
        //（keep.log 靠 ! 否定留下来：没有那条否定它会被 *.log 吃掉）
        GlobOutcome outcome = await new SimpleGlobber(_dir).SearchAsync("**/*.*", path: "Code",
            ct: TestContext.Current.CancellationToken);

        Assert.Null(outcome.Failure);
        Assert.Equal(["Code/lib/l.txt", "Code/src/a.txt", "Code/src/keep.log", "Code/top.txt"], Files(outcome));
    }

    /// <summary>点名要搜被忽略的目录时照搜——目标自己被上层规则忽略，上层规则整体让路</summary>
    [Fact]
    public async Task ExplicitlyIgnoredDirectory_IsStillSearched()
    {
        GlobOutcome outcome = await new SimpleGlobber(_dir).SearchAsync("**/*.txt", path: "Code/build",
            ct: TestContext.Current.CancellationToken);

        Assert.Null(outcome.Failure);
        Assert.Equal(["Code/build/b.txt"], Files(outcome));
    }

    /// <summary>Unity 工程：Library 整棵剪掉（下探都不进），Assets 正常命中</summary>
    [Fact]
    public async Task UnityLibrary_IsPrunedButAssetsKept()
    {
        GlobOutcome outcome = await new SimpleGlobber(_dir).SearchAsync("**/*.*",
            ct: TestContext.Current.CancellationToken);

        Assert.Null(outcome.Failure);
        Assert.DoesNotContain(outcome.Entries, x => x.Path.StartsWith("Game/Library/", StringComparison.Ordinal));
        Assert.Contains("Game/Assets/Scripts/Player.cs", outcome.Entries.Select(x => x.Path));
        Assert.Contains("Game/Assets/Scenes/Main.unity", outcome.Entries.Select(x => x.Path));
    }

    /// <summary>没有 ignore 文件的作用域：以前的行为不变，全收</summary>
    [Fact]
    public async Task NoIgnoreFiles_KeepsPreviousBehavior()
    {
        string plain = Directory.CreateTempSubdirectory("uiharu-glob-plain-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(plain, "a.txt"), "x");
            Directory.CreateDirectory(Path.Combine(plain, "sub"));
            File.WriteAllText(Path.Combine(plain, "sub", "b.txt"), "x");

            GlobOutcome outcome = await new SimpleGlobber(plain).SearchAsync("**/*.txt",
                ct: TestContext.Current.CancellationToken);

            Assert.Null(outcome.Failure);
            Assert.Equal(["a.txt", "sub/b.txt"], Files(outcome));
        }
        finally
        {
            try
            {
                Directory.Delete(plain, recursive: true);
            }
            catch
            {
                // 临时目录清理失败不影响断言
            }
        }
    }

    private void Write(string relative)
    {
        string path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    private static void WriteTo(string root, string relative)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    private static void DeleteQuietly(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // 临时目录清理失败不影响断言
        }
    }

    /// <summary>单文件作用域不套上层规则：根忽略 <c>*.log</c>，点名搜文件仍能搜到</summary>
    [Fact]
    public async Task SingleFileScope_DoesNotApplyAncestorRules()
    {
        string root = Directory.CreateTempSubdirectory("uiharu-glob-single-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, ".gitignore"), "*.log\n");
            WriteTo(root, "Code/src/keep.log");

            GlobOutcome outcome = await new SimpleGlobber(root).SearchAsync("**/*.log", path: "Code/src/keep.log",
                ct: TestContext.Current.CancellationToken);

            Assert.Null(outcome.Failure);
            Assert.Equal(["Code/src/keep.log"], Files(outcome));
        }
        finally
        {
            DeleteQuietly(root);
        }
    }

    /// <summary>跨文件层级的"最近优先"：根忽略 <c>*.log</c>，子目录的 <c>!keep.log</c> 把它救回来</summary>
    [Fact]
    public async Task NestedNegation_NearestFileWins()
    {
        string root = Directory.CreateTempSubdirectory("uiharu-glob-neg-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, ".gitignore"), "*.log\n");
            Directory.CreateDirectory(Path.Combine(root, "Code"));
            File.WriteAllText(Path.Combine(root, "Code", ".gitignore"), "!keep.log\n");
            WriteTo(root, "Code/keep.log");
            WriteTo(root, "Code/drop.log");

            GlobOutcome outcome = await new SimpleGlobber(root).SearchAsync("**/*.log",
                ct: TestContext.Current.CancellationToken);

            Assert.Null(outcome.Failure);
            Assert.Equal(["Code/keep.log"], Files(outcome));
        }
        finally
        {
            DeleteQuietly(root);
        }
    }

    /// <summary><c>collectStats</c> 的全量计数只含非忽略条目：被剪掉的不进 <c>TotalHits</c></summary>
    [Fact]
    public async Task CollectStats_CountsOnlyNonIgnored()
    {
        string root = Directory.CreateTempSubdirectory("uiharu-glob-stats-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, ".gitignore"), "skip/\n");
            WriteTo(root, "a.txt");
            WriteTo(root, "sub/c.txt");
            WriteTo(root, "skip/b.txt");

            GlobOutcome outcome = await new SimpleGlobber(root).SearchAsync("**/*.txt", collectStats: true,
                ct: TestContext.Current.CancellationToken);

            Assert.Null(outcome.Failure);
            Assert.False(outcome.Truncated);
            Assert.Equal(2, outcome.TotalHits);
            Assert.Equal(["a.txt", "sub/c.txt"], Files(outcome));
        }
        finally
        {
            DeleteQuietly(root);
        }
    }

    private static List<string> Files(GlobOutcome outcome) =>
        outcome.Entries.Select(x => x.Path.Replace('\\', '/')).Order(StringComparer.Ordinal).ToList();
}
