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
        Assert.StartsWith("已搜索", result);
        Assert.Contains("0 条命中", result);
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

        // 文件一行一条:path (size),不带类型前缀
        Assert.StartsWith("a.txt (", result);
        Assert.DoesNotContain("[FILE]", result);
        Assert.Contains("sub/b.txt", result);
        // 有命中、没截断 → 说明为 null,正文原样返回,没有分隔段
        Assert.DoesNotContain("---", result);
    }

    /// <summary>
    /// 超过内联上限触发过宽：不再罗列半截路径列表（几千 token），
    /// 换成按目录聚合的统计 + 过宽提示。统计基于全量命中（不是保下来的前缀）。
    /// </summary>
    [Fact]
    public async Task Glob_OverLimit_ReportsDirectoryCounts()
    {
        for (int i = 0; i < 301; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(_dir, $"f{i:D4}.txt"), "", TestContext.Current.CancellationToken);
        }

        string result = await _tools.Glob("*.txt", ct: TestContext.Current.CancellationToken);

        // 过宽正文先声明「不是文件列表」，再给目录统计行（根目录 "." 下 301 个文件）
        Assert.StartsWith("命中过多，下面是按目录的分布，不是文件列表：\n.: 301 条", result);
        Assert.DoesNotContain("[FILE]", result);
        Assert.Contains("范围太宽", result);
        Assert.Contains("共 301 条命中", result);
        Assert.Contains(ToolResultText.NarrowGuidance, result);
    }

    /// <summary>过宽时给目录统计，且统计自全量（不是内联上限内的前缀）</summary>
    [Fact]
    public async Task Glob_OverLimit_CountsPerDirectory()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "sub1"));
        Directory.CreateDirectory(Path.Combine(_dir, "sub2"));
        for (int i = 0; i < 180; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(_dir, "sub1", $"a{i:D3}.txt"), "", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(_dir, "sub2", $"b{i:D3}.txt"), "", TestContext.Current.CancellationToken);
        }

        string result = await _tools.Glob("**/*.txt", ct: TestContext.Current.CancellationToken);

        // 360 条命中、两个目录各 180：展示目录统计，路径列表不出现
        // （分隔行前的正文逐行取，排除分隔后的 notice 段与空行）
        string[] statLines = StatLines(result);
        Assert.Equal(2, statLines.Length);
        Assert.Contains("sub1: 180 条", statLines);
        Assert.Contains("sub2: 180 条", statLines);
        Assert.Contains("共 360 条命中", result);
        Assert.DoesNotContain("[FILE]", result);
    }

    /// <summary>等重的父目录一起展开放不下时都不拆（360 命中、60 子目录分 3 父 → 3 组）</summary>
    [Fact]
    public async Task Glob_OverLimit_GoesUpALevel()
    {
        for (int p = 0; p < 3; p++)
        {
            string parent = Path.Combine(_dir, $"gdir{(char)('A' + p)}");
            for (int s = 0; s < 20; s++)
            {
                string sub = Path.Combine(parent, $"sub{s:D2}");
                Directory.CreateDirectory(sub);
                for (int f = 0; f < 6; f++)
                {
                    await File.WriteAllTextAsync(Path.Combine(sub, $"f{f}.txt"), "", TestContext.Current.CancellationToken);
                }
            }
        }

        string result = await _tools.Glob("**/*.txt", ct: TestContext.Current.CancellationToken);

        // 360 条命中分布在 60 个不同子目录 → 3 个父目录一起展开要 60 组，超限 → 停在 3 组
        string[] statLines = StatLines(result);
        Assert.Equal(3, statLines.Length);
        Assert.Contains("gdirA: 120 条", statLines);
        Assert.Contains("gdirB: 120 条", statLines);
        Assert.Contains("gdirC: 120 条", statLines);
        Assert.Contains("已按目录聚合为 3 组", result);
        Assert.DoesNotContain("[FILE]", result);
    }

    /// <summary>统计触顶（TotalHits 只是下限）时库层标志 + 渲染文案都应说"至少 N"而不是假精确</summary>
    [Fact]
    public async Task Glob_StatsCapped_ReportsAtLeast()
    {
        // 110 个文件，统计上限 100：TotalHits=100、TotalHitsCapped=true
        var root = Directory.CreateTempSubdirectory("uiharu-capped-").FullName;
        try
        {
            for (int i = 0; i < 110; i++)
            {
                await File.WriteAllTextAsync(Path.Combine(root, $"f{i:D3}.txt"), "", TestContext.Current.CancellationToken);
            }

            GlobOutcome outcome = await new SimpleGlobber(root)
                .SearchAsync("*.txt", collectStats: true, statsCap: 100, ct: TestContext.Current.CancellationToken);

            Assert.True(outcome.Truncated);
            Assert.True(outcome.TotalHitsCapped);
            Assert.Equal(100, outcome.TotalHits);

            // 渲染层把触顶转成 "N+"，不让模型把 100 当成全量总数
            string rendered = PermissiveFileAccessTools.RenderDirectorySummary(outcome);
            Assert.Contains("共 100+ 条命中", rendered);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>未触顶时渲染仍是精确的"共 N 条命中"（过宽但统计没到上限）</summary>
    [Fact]
    public async Task Glob_StatsNotCapped_ReportsExactCount()
    {
        // 350 个文件（>300 触发过宽），统计上限 1000（不触顶）：TotalHits=350、TotalHitsCapped=false
        var root = Directory.CreateTempSubdirectory("uiharu-exact-").FullName;
        try
        {
            for (int i = 0; i < 350; i++)
            {
                await File.WriteAllTextAsync(Path.Combine(root, $"g{i:D3}.txt"), "", TestContext.Current.CancellationToken);
            }

            GlobOutcome outcome = await new SimpleGlobber(root)
                .SearchAsync("*.txt", collectStats: true, statsCap: 1000, ct: TestContext.Current.CancellationToken);

            Assert.True(outcome.Truncated);
            Assert.False(outcome.TotalHitsCapped);
            Assert.Equal(350, outcome.TotalHits);

            string rendered = PermissiveFileAccessTools.RenderDirectorySummary(outcome);
            Assert.Contains("共 350 条命中", rendered);
            Assert.DoesNotContain("+", rendered);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>过宽结果的统计行：跳过开头的声明行，取到分隔行为止</summary>
    private static string[] StatLines(string result) => result.Split('\n')
        .Skip(1)
        .TakeWhile(line => !line.StartsWith("---"))
        .Where(line => line.Length > 0)
        .ToArray();
}
