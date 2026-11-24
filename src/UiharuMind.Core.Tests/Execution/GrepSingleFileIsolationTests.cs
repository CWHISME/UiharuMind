using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 钉死单文件搜索的隔离性：指定 path 为某个文件时，父目录里的同名文件不能吃掉命中配额，
/// 也不能把命中串进结果。引擎在枚举阶段就按完整路径过滤，这里验证上层口径一致。
/// </summary>
public class GrepSingleFileIsolationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uiharu-grep-single-").FullName;
    private readonly SimpleGrepper _grepper;

    public GrepSingleFileIsolationTests()
    {
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

    [Fact]
    public async Task Grep_SingleFile_IgnoresSameNamedFilesInSiblingDirs()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        // 同名文件分布在多个子目录，每个都塞满 TARGET 命中
        for (int i = 0; i < 6; i++)
        {
            string sub = Path.Combine(_dir, $"sub{i}");
            Directory.CreateDirectory(sub);
            await File.WriteAllTextAsync(Path.Combine(sub, "target.txt"),
                string.Concat(Enumerable.Range(0, 200).Select(j => $"TARGET {i}-{j}\n")), ct);
        }

        // 目标文件反而只有一条命中
        string target = Path.Combine(_dir, "target.txt");
        await File.WriteAllTextAsync(target, "only-one TARGET here\n", ct);

        GrepOutcome outcome = await _grepper.SearchAsync("TARGET", path: target, ct: ct);

        Assert.Null(outcome.Failure);
        Assert.False(outcome.ReachedLimit);
        Assert.Single(outcome.Matches);
        Assert.Contains("only-one TARGET here", outcome.Matches[0].Snippet);
    }
}
