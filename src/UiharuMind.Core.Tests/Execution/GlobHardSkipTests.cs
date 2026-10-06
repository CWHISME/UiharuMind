using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉死 <c>SimpleGlobber</c> 的硬排除。它曾经写成 <c>**/node_modules/**</c> 并把整条路径
/// 当一个参数传给 <c>GlobCollection.IsMatch</c>，两处都让匹配恒为 false——
/// 排除声明得清清楚楚却一次都没生效，而这种失效在实机上只表现为"搜索有点慢"。
/// </summary>
public class GlobHardSkipTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "uiharu-glob-skip-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private void WriteFile(string relativePath)
    {
        string full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
    }

    [Theory]
    [InlineData("node_modules")]
    [InlineData("bin")]
    [InlineData("obj")]
    public async Task Search_SkipsHardExcludedDirectories(string excluded)
    {
        WriteFile($"src/{excluded}/buried.txt");
        WriteFile("src/kept.txt");

        GlobOutcome outcome = await new SimpleGlobber(_root).SearchAsync("**/*.txt", ct: TestContext.Current.CancellationToken);

        Assert.Null(outcome.Failure);
        Assert.Equal(["src/kept.txt"], outcome.Entries.Select(x => x.Path));
    }

    /// <summary>
    /// 点开头的条目默认不搜，pattern 点名了才放行（Unix 上它们曾因 Hidden 属性被一律跳过，点名也搜不到）
    /// </summary>
    [Fact]
    public async Task Search_DotEntriesOnlyWhenPatternNamesThem()
    {
        WriteFile("x/.uiharu/a.txt");
        WriteFile("x/.other/b.txt");
        WriteFile("x/kept.txt");
        var globber = new SimpleGlobber(_root);
        CancellationToken ct = TestContext.Current.CancellationToken;

        GlobOutcome plain = await globber.SearchAsync("**/*.txt", ct: ct);
        GlobOutcome named = await globber.SearchAsync("**/.uiharu/**", ct: ct);

        Assert.Equal(["x/kept.txt"], plain.Entries.Select(x => x.Path));
        Assert.Equal(["x/.uiharu/a.txt"], named.Entries.Select(x => x.Path));
    }

    /// <summary>
    /// 一条都命中不了时枚举器不会把控制权交回循环，取消必须在遍历回调里生效——
    /// 否则用户点停止也要等它把整个目录树（如 ~ 下 <c>**/.xxx/**</c>）走完
    /// </summary>
    [Fact]
    public async Task Search_CancelledWithoutAnyMatch_Throws()
    {
        WriteFile("a/b/c.txt");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new SimpleGlobber(_root).SearchAsync("**/*.none", ct: cts.Token));
    }
}
