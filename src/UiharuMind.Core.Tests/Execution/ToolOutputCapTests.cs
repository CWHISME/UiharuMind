using System.Text;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 钉死工具输出限幅：工具输出直接进模型上下文，编码会话的上下文大头是工具结果。
/// Read 的上限必须由工具侧强制(不能指望模型自觉传 limit)，Grep 的命中数必须封顶。
/// </summary>
public class ToolOutputCapTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uiharu-caps-").FullName;
    private readonly PermissiveFileAccessTools _tools;

    public ToolOutputCapTests()
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
    public async Task Read_WithoutLimit_IsCappedWithContinuationHint()
    {
        string path = Path.Combine(_dir, "big.txt");
        await File.WriteAllLinesAsync(path, Enumerable.Range(1, 2500).Select(i => $"line{i}"), TestContext.Current.CancellationToken);

        string result = await _tools.Read(path, cancellationToken: TestContext.Current.CancellationToken);

        string[] lines = result.Split('\n');
        Assert.Equal(FileReader.DefaultLineLimit + 1, lines.Length); //窗口 + 截断提示行
        Assert.Equal("line1", lines[0]);
        Assert.Contains($"offset={FileReader.DefaultLineLimit + 1}", lines[^1]);
    }

    [Fact]
    public async Task Read_Offset_ContinuesFromWhereTruncationPointed()
    {
        string path = Path.Combine(_dir, "big2.txt");
        await File.WriteAllLinesAsync(path, Enumerable.Range(1, 2500).Select(i => $"line{i}"), TestContext.Current.CancellationToken);

        string result = await _tools.Read(path, offset: FileReader.DefaultLineLimit + 1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.StartsWith($"line{FileReader.DefaultLineLimit + 1}", result);
        Assert.DoesNotContain("[truncated", result); //剩余 500 行在窗口内,不应再截断
    }

    [Fact]
    public async Task Read_OverlongLine_IsTruncatedInline()
    {
        string path = Path.Combine(_dir, "minified.js");
        await File.WriteAllTextAsync(path, new string('x', 50_000), TestContext.Current.CancellationToken);

        string result = await _tools.Read(path, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("…[已截断]", result);
        Assert.True(result.Length < 3000, $"单行截断后总长应远小于原文,实际 {result.Length}");
    }

    /// <summary>
    /// 总量上限按 UTF-8 字节算,而不是字符——中文一个字符三字节,按字符算会让中文文件
    /// 实际放进三四倍于标称的 token。传 head=4000 绕过行数限制,让 1MB 字节上限先生效。
    /// </summary>
    [Fact]
    public async Task Read_ChineseFile_IsCappedByBytesNotChars()
    {
        string path = Path.Combine(_dir, "chinese.md");
        // 每行 100 个汉字 = 301 字节,4000 行约 1.2MB,传 head=4000 绕过行数限制
        await File.WriteAllLinesAsync(path,
            Enumerable.Range(1, 4000).Select(_ => new string('测', 100)), TestContext.Current.CancellationToken);

        string result = await _tools.Read(path, head: 4000, cancellationToken: TestContext.Current.CancellationToken);

        string[] lines = result.Split('\n');
        Assert.Contains("continue with offset=", lines[^1]);
        Assert.True(lines.Length - 1 < 4000, $"应因字节上限提前截断,实际返回 {lines.Length - 1} 行");
        Assert.True(Encoding.UTF8.GetByteCount(result) < FileReader.MaxTotalBytes * 2,
            "截断后总字节应在上限量级内");
    }

    /// <summary>
    /// head=-1 是全文读模式:绕过 1MB 字节上限,读完整个文件。
    /// 用于需要完整理解文件以做重构的场景——分段读容易让模型忘了前面读了什么。
    /// </summary>
    [Fact]
    public async Task Read_WithLimitNegativeOne_ReadsEntireFile()
    {
        string path = Path.Combine(_dir, "full.txt");
        // 3000 行 × 约 500 字节/行 = 1.5MB,超过 1MB 字节上限
        await File.WriteAllLinesAsync(path,
            Enumerable.Range(1, 3000).Select(i => new string('x', 480) + i), TestContext.Current.CancellationToken);

        string result = await _tools.Read(path, head: -1, cancellationToken: TestContext.Current.CancellationToken);

        // 全文读绕过字节上限,不应有截断提示
        Assert.DoesNotContain("[truncated", result);
        Assert.DoesNotContain("continue with offset=", result);
        // 应包含最后一行
        Assert.Contains("3000", result);
        // 总字节应超过 1MB(证明绕过了字节上限)
        Assert.True(Encoding.UTF8.GetByteCount(result) > FileReader.MaxTotalBytes,
            "head=-1 应绕过字节上限,实际返回字节应超过上限");
    }

    [Fact]
    public async Task Grep_Matches_AreCappedWithSentinel()
    {
        string path = Path.Combine(_dir, "haystack.txt");
        await File.WriteAllLinesAsync(path, Enumerable.Range(1, 300).Select(i => $"needle {i}"), TestContext.Current.CancellationToken);

        string result = await _tools.Grep("needle", ct: TestContext.Current.CancellationToken);

        // 300 处命中来自同一文件 → 路径行 + 封顶 200 条命中行,后面的被丢;说明拼接在正文后面的分隔段里
        Assert.StartsWith("haystack.txt\n", result);
        string body = result.Split("\n\n---\n", 2)[0]; //分隔行前的才是正文,说明不算行数
        Assert.Equal(GrepResultShaper.MaxMatches + 1, body.Split('\n').Count(line => line.Length > 0));
        Assert.DoesNotContain("needle 300", result);
        Assert.Contains("共 300 处命中", result); //总数统计:300 = 已展示 200 + 丢弃 100
        Assert.Contains("另有 100 处", result);
        Assert.Contains("收窄查询", result);
    }

    /// <summary>
    /// contextLines 曾是个假参数:一路传进了引擎,转换时只取命中行,上下文全丢。
    /// 模型要了上下文却拿回孤零零一行,只能再发一次 Read——这条测试就是那个坑的看门人。
    /// </summary>
    [Fact]
    public async Task Grep_WithContextLines_ReturnsSurroundingLines()
    {
        string path = Path.Combine(_dir, "ctx.txt");
        await File.WriteAllLinesAsync(path, ["l1", "l2", "target", "l4", "l5"], TestContext.Current.CancellationToken);

        string result = await _tools.Grep("target", contextLines: 1, ct: TestContext.Current.CancellationToken);

        // 正文保持 grep 味:路径行 + 命中行 "N:content"、上下文行 "N-content"(对齐 ripgrep);
        // 总数统计拼在分隔行后的说明段,不污染正文
        Assert.StartsWith("ctx.txt\n2-l2\n3:target\n4-l4", result);
        Assert.Contains("共 1 处命中，跨 1 个文件", result);
    }

    /// <summary>同一文件的多处命中,重叠上下文行只出现一次;行号升序</summary>
    [Fact]
    public async Task Grep_AggregatesPerFile_AndDeduplicatesOverlappingContext()
    {
        string path = Path.Combine(_dir, "dense.txt");
        await File.WriteAllLinesAsync(path, ["hit", "hit", "hit"], TestContext.Current.CancellationToken);

        string result = await _tools.Grep("hit", contextLines: 2, ct: TestContext.Current.CancellationToken);

        // 三处命中 + 重叠上下文去重后,正文仍是 1/2/3 三行,按行号升序(路径行独占一行)
        Assert.StartsWith("dense.txt\n1:hit\n2:hit\n3:hit", result);
        Assert.Contains("共 3 处命中", result);
    }

    /// <summary>
    /// 命中过多(正文超过 32KB 字节预算)时不返半截正文,整页换成命中地图。
    /// 半截正文按扫描序取、无相关度排序,留着只会让模型锚定到运气好的文件上;
    /// 地图给全量分布(不随 200 上限截短),模型据它定点 Read 或收窄重搜。
    /// </summary>
    [Fact]
    public async Task Grep_OverByteBudget_SwitchesToHitMap()
    {
        string path = Path.Combine(_dir, "wide.txt");
        // 400 处命中 × ~290 字节 = 约 116KB;200 上限封顶后正文仍有约 58KB,远超 32KB 预算
        await File.WriteAllLinesAsync(path,
            Enumerable.Range(1, 400).Select(i => "needle " + new string('x', 280) + i), TestContext.Current.CancellationToken);

        // 命中过多（正文超 32KB 字节预算）时不返半截正文，整页换成命中地图。
        // 地图渲成文本行：每文件一行（命中数 + 行号区间），说明拼在后段。
        // 半截正文按扫描序取、无相关度排序，留着只会让模型锚定到运气好的文件上；
        // 地图给全量分布（不随 200 上限截短），模型据它定点 Read 或收窄重搜。
        string result = await _tools.Grep("needle", ct: TestContext.Current.CancellationToken);

        Assert.Contains("wide.txt: 400 处命中（第 1–400 行）", result); //地图给全量分布,不被 200 上限截短
        Assert.Contains("处命中，跨", result);
        Assert.Contains("无法内联返回", result);
        Assert.Contains("---\n400 处命中，跨", result); //地图说明与正文模式同款分隔行(统一口径)
        Assert.DoesNotContain("---\n只展示前", result); //地图模式不再走 200 上限正文
    }

    /// <summary>地图按命中数降序取 Top 50:命中极度分散本身就是"搜宽了"的信号,看前 50 就够下判断</summary>
    [Fact]
    public async Task Grep_Map_IsTop50ByHits()
    {
        for (int f = 0; f < 60; f++)
        {
            string path = Path.Combine(_dir, $"f{f:D2}.txt");
            // 10 处命中/文件 × 约 260 字节 = 200 上限封顶后正文约 52KB,触发地图模式
            await File.WriteAllLinesAsync(path,
                Enumerable.Range(1, 10).Select(i => "needle " + new string('y', 250) + i), TestContext.Current.CancellationToken);
        }

        string result = await _tools.Grep("needle", ct: TestContext.Current.CancellationToken);

        Assert.Contains("600 处命中，跨 60 个文件", result);
        // Top 50 按命中数降序,每文件一行文本;拿到的是保出来的 50 个文件名
        // (过滤时匹配“: N 处命中（第”而不是“处命中”，避免把说明行“600 处命中，跨”也算进去)
        string[] mapLines = result.Split('\n').Where(line => line.Contains("处命中（第")).ToArray();
        Assert.Equal(GrepResultShaper.MaxMapFiles, mapLines.Length);
        Assert.All(mapLines, line => Assert.Contains(": 10 处命中（第 ", line));
    }

    [Fact]
    public void TruncateLine_RespectsBudget()
    {
        Assert.Equal("short", ToolOutputTruncation.TruncateLine("short", 10));
        string truncated = ToolOutputTruncation.TruncateLine(new string('a', 100), 10);
        Assert.StartsWith("aaaaaaaaaa …[已截断]", truncated);
    }
}
