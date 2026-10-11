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
        Assert.StartsWith("Searched", result);
        Assert.Contains("0 matches", result);
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

        Assert.Contains("does not compile", result);
        Assert.Contains("searched as a literal string", result);
    }

    [Fact]
    public async Task Grep_NormalisedLeadingWildcard_IsReportedInText()
    {
        string result = await _tools.Grep("*Globber", isRegex: true, ct: TestContext.Current.CancellationToken);

        Assert.Contains("normalised to", result);
        Assert.Contains(".*Globber", result);
    }
}