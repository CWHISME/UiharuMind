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

        GlobOutcome outcome = await new SimpleGlobber(_root).SearchAsync("**/*.txt");

        Assert.Null(outcome.Failure);
        Assert.Equal(["src/kept.txt"], outcome.Entries.Select(x => x.Path));
    }
}
