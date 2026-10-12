using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// Grep 工具回给模型的<b>纯文本</b>形状（Shape 现在返回 string，正文与说明拼在同段文本里）：
/// 失败、0 命中、自动降级、归一化这些非命中输出是模型唯一读得到的形迹，逐一钉住，
/// 免得哪天改成没头没尾的碎句。
/// </summary>
public class GrepToolTextOutputTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uiharu-grep-text-").FullName;
    private readonly PermissiveFileAccessTools _tools;

    public GrepToolTextOutputTests()
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
    public async Task Grep_ZeroMatches_ReturnsNoticeOnly_NoLeadingBlankLines()
    {
        string result = await _tools.Grep("definitely-not-here", ct: TestContext.Current.CancellationToken);

        // 没有正文可分隔:直接给说明,不带分隔行、不以空行开头
        Assert.StartsWith("已搜索", result);
        Assert.Contains("0 处命中", result);
        Assert.DoesNotContain("\n\n---", result);
    }

    [Fact]
    public async Task Grep_PathNotFound_ReturnsResolvedFailureText()
    {
        string result = await _tools.Grep("x", path: "no/such/dir", ct: TestContext.Current.CancellationToken);

        // 必须回显真实解析结果(ADR 0017):模型看得到自己的相对路径被拼成了哪里,才不用乱猜
        Assert.Contains("找不到路径", result);
        Assert.Contains("no/such/dir", result);
        Assert.Contains("解析为", result);
    }

    [Fact]
    public async Task Grep_RegexFallback_IsReportedInText()
    {
        // "[" 在 .NET 正则里编译不过 → 降级字面串;降级必须说,不说模型会对"少了几条命中"推错
        string result = await _tools.Grep("[", isRegex: true, ct: TestContext.Current.CancellationToken);

        Assert.Contains("编译不成受支持的正则", result);
        Assert.Contains("按字面串重搜", result);
    }

    [Fact]
    public async Task Grep_NormalisedLeadingWildcard_IsReportedInText()
    {
        string result = await _tools.Grep("*Globber", isRegex: true, ct: TestContext.Current.CancellationToken);

        Assert.Contains("模式已归一化为", result);
        Assert.Contains(".*Globber", result);
    }

    /// <summary>多文件命中时路径行就是文件边界:正文在文件之间只有一个换行,没有空行/分隔行;
    /// 命中总数统计拼在分隔行后的说明段</summary>
    [Fact]
    public async Task Grep_MultiFile_Body_UsesPathLineAsFileBoundary()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "a.txt"), "needle one\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_dir, "b.txt"), "needle two\n", TestContext.Current.CancellationToken);

        string result = await _tools.Grep("needle", ct: TestContext.Current.CancellationToken);

        // 文件路径独占一行(文件边界),下面接命中行;两个文件之间没有空行
        Assert.Contains("a.txt\n1:needle one", result);
        Assert.Contains("b.txt\n1:needle two", result);
        Assert.DoesNotContain("needle one\n\n", result);
        // 命中总数在说明段,与正文用分隔行隔开
        Assert.Contains("---\n共 2 处命中，跨 2 个文件", result);
    }

    /// <summary>多文件命中按文件路径字典序输出,不随扫描顺序变——输出稳定,逐文件扫读顺序可预期</summary>
    [Fact]
    public async Task Grep_MultiFile_Body_IsOrderedByFilePath()
    {
        // 故意逆字典序创建:z 先写、a 后写,让扫描顺序与字典序相反
        await File.WriteAllTextAsync(Path.Combine(_dir, "z.txt"), "needle\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_dir, "a.txt"), "needle\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_dir, "m.txt"), "needle\n", TestContext.Current.CancellationToken);

        string result = await _tools.Grep("needle", ct: TestContext.Current.CancellationToken);

        int a = result.IndexOf("a.txt\n", StringComparison.Ordinal);
        int m = result.IndexOf("m.txt\n", StringComparison.Ordinal);
        int z = result.IndexOf("z.txt\n", StringComparison.Ordinal);
        Assert.True(a >= 0 && m >= 0 && z >= 0, "三个文件都应命中");
        Assert.True(a < m && m < z, $"正文应按文件路径字典序,实际位置:{a},{m},{z}");
    }

    /// <summary>caseSensitive 参数恢复:传 true 时精确匹配(标准 grep/ripgrep 的 -s 语义),不传保持不区分</summary>
    [Fact]
    public async Task Grep_CaseSensitive_OnlyMatchesExactCase()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "case.txt"), "Needle\nneedle\n",
            TestContext.Current.CancellationToken);

        string insensitive = await _tools.Grep("needle", ct: TestContext.Current.CancellationToken);
        Assert.Contains("1:Needle", insensitive);
        Assert.Contains("2:needle", insensitive);

        string sensitive = await _tools.Grep("needle", caseSensitive: true, ct: TestContext.Current.CancellationToken);
        Assert.DoesNotContain("Needle", sensitive);
        Assert.Contains("2:needle", sensitive);
    }

    /// <summary>中文字符一字符三字节,会显著提前触发 32KB 字节预算——正文按 UTF-8 字节计,不是字符数</summary>
    [Fact]
    public async Task Grep_ChineseOverByteBudget_SwitchesToHitMap()
    {
        // 100 行 × (needle + 200 汉字) ≈ 61KB,远超 32KB 预算 → 整页换命中地图
        await File.WriteAllLinesAsync(Path.Combine(_dir, "cn.txt"),
            Enumerable.Range(1, 100).Select(i => "needle " + new string('测', 200)),
            TestContext.Current.CancellationToken);

        string result = await _tools.Grep("needle", ct: TestContext.Current.CancellationToken);

        Assert.Contains("100 处命中，跨 1 个文件", result);
        Assert.Contains("无法内联返回", result);
        Assert.Contains("cn.txt: 100 处命中（第 1–100 行）", result);
    }
}