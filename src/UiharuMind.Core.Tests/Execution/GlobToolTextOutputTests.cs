using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// Glob 工具回给模型的纯文本形状（与 Grep 同口径）：条目每行一个、说明拼在分隔行后；
/// 失败与 0 命中直接给说明文本，不混进条目、也不加没有正文的分隔。
/// </summary>
public class GlobToolTextOutputTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uiharu-glob-text-").FullName;
    private readonly PermissiveFileAccessTools _tools;

    public GlobToolTextOutputTests()
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

    [Fact]
    public async Task Glob_ZeroMatches_ReturnsNoticeOnly_NoLeadingBlankLines()
    {
        string result = await _tools.Glob("**/*.never", ct: TestContext.Current.CancellationToken);

        // 没有条目可分隔:直接给说明,不带分隔行、不以空行开头
        Assert.StartsWith("Searched", result);
        Assert.Contains("0 matches", result);
        Assert.DoesNotContain("\n\n---", result);
    }

    [Fact]
    public async Task Glob_PathNotFound_ReturnsResolvedFailureText()
    {
        string result = await _tools.Glob("**/*.cs", path: "no/such/dir", ct: TestContext.Current.CancellationToken);

        // 必须回显真实解析结果(ADR 0017):模型看得到自己的相对路径被拼成了哪里,才不用乱猜
        Assert.Contains("找不到路径", result);
        Assert.Contains("no/such/dir", result);
        Assert.Contains("解析为", result);
    }

    [Fact]
    public async Task Glob_Entries_AreOnePerLine_NoNoticeWithoutReason()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "a.txt"), "x", TestContext.Current.CancellationToken);
        Directory.CreateDirectory(Path.Combine(_dir, "sub"));
        await File.WriteAllTextAsync(Path.Combine(_dir, "sub", "b.txt"), "y", TestContext.Current.CancellationToken);

        string result = await _tools.Glob("**/*.txt", ct: TestContext.Current.CancellationToken);

        // 条目带类型前缀:文件可行动,格式 [FILE] path (size)
        Assert.Contains("[FILE] a.txt (", result);
        Assert.Contains("sub/b.txt", result);
        // 有命中、没截断 → 说明为 null,正文原样返回,没有分隔段
        Assert.DoesNotContain("---", result);
    }
}