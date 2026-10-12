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
        Assert.Equal("   1→line1", lines[0]);
        Assert.Contains($"offset={FileReader.DefaultLineLimit + 1}", lines[^1]);
    }

    [Fact]
    public async Task Read_Offset_ContinuesFromWhereTruncationPointed()
    {
        string path = Path.Combine(_dir, "big2.txt");
        await File.WriteAllLinesAsync(path, Enumerable.Range(1, 2500).Select(i => $"line{i}"), TestContext.Current.CancellationToken);

        string result = await _tools.Read(path, offset: FileReader.DefaultLineLimit + 1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.StartsWith($"{FileReader.DefaultLineLimit + 1}→line{FileReader.DefaultLineLimit + 1}", result);
        Assert.DoesNotContain("[已截断：", result); //剩余 500 行在窗口内,不应再截断
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
        Assert.Contains("续读传 offset=", lines[^1]);
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
        Assert.DoesNotContain("[已截断：", result);
        Assert.DoesNotContain("续读传 offset=", result);
        // 应包含最后一行
        Assert.Contains("3000", result);
        // 总字节应超过 1MB(证明绕过了字节上限)
        Assert.True(Encoding.UTF8.GetByteCount(result) > FileReader.MaxTotalBytes,
            "head=-1 应绕过字节上限,实际返回字节应超过上限");
    }

    /// <summary>
    /// 命中超过 200 处上限时不再返半截正文（半截按扫描序取、无相关度排序，模型会锚定到
    /// 运气好的文件上），整页换成命中地图：每文件一行（命中数 + 前几个行号），按全量统计。
    /// </summary>
    [Fact]
    public async Task Grep_OverMaxMatches_SwitchesToHitMap()
    {
        string path = Path.Combine(_dir, "haystack.txt");
        await File.WriteAllLinesAsync(path, Enumerable.Range(1, 300).Select(i => $"needle {i}"), TestContext.Current.CancellationToken);

        string result = await _tools.Grep("needle", ct: TestContext.Current.CancellationToken);

        // 300 处命中全在同一文件 → 先声明「不是正文」，再给全量分布，不返 200 行半截正文
        Assert.StartsWith("命中过多，下面是按文件的命中分布，不是匹配内容：\nhaystack.txt: 300 处（行 1, 2, 3, 4, 5, …）", result);
        Assert.Contains("300 处命中，跨 1 个文件", result);
        Assert.Contains("太宽，无法内联返回", result);
        Assert.DoesNotContain("needle 300", result); //地图无 snippet：没有命中行正文
        Assert.DoesNotContain("---\n只展示前", result);
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
    /// 命中过多(正文超过 16KB 字节预算)时不返半截正文,整页换成命中地图。
    /// 半截正文按扫描序取、无相关度排序,留着只会让模型锚定到运气好的文件上;
    /// 地图给全量分布(不随 200 上限截短),模型据它定点 Read 或收窄重搜。
    /// </summary>
    [Fact]
    public async Task Grep_OverByteBudget_SwitchesToHitMap()
    {
        string path = Path.Combine(_dir, "wide.txt");
        // 400 处命中 × ~290 字节 = 约 116KB;200 上限封顶后正文仍有约 58KB,远超 16KB 预算
        await File.WriteAllLinesAsync(path,
            Enumerable.Range(1, 400).Select(i => "needle " + new string('x', 280) + i), TestContext.Current.CancellationToken);

        // 命中过多（正文超 16KB 字节预算）时不返半截正文，整页换成命中地图。
        // 地图渲成文本行：每文件一行（命中数 + 前几个行号），说明拼在后段。
        // 半截正文按扫描序取、无相关度排序，留着只会让模型锚定到运气好的文件上；
        // 地图给全量分布（不随 200 上限截短），模型据它定点 Read 或收窄重搜。
        string result = await _tools.Grep("needle", ct: TestContext.Current.CancellationToken);

        Assert.Contains("wide.txt: 400 处（行 1, 2, 3, 4, 5, …）", result); //地图给全量分布,不被 200 上限截短
        Assert.Contains("处命中，跨", result);
        Assert.Contains("无法内联返回", result);
        Assert.Contains("---\n400 处命中，跨", result); //地图说明与正文模式同款分隔行(统一口径)
        Assert.DoesNotContain("---\n只展示前", result); //地图模式不再走 200 上限正文
    }

    /// <summary>文件数超上限时不再给文件级列表，按目录前缀聚合到 ≤ 上限（120 个文件全在根下 → 1 组）</summary>
    [Fact]
    public async Task Grep_Map_TooManyFiles_GoesUpToDirectoryLevel()
    {
        for (int f = 0; f < 120; f++)
        {
            string path = Path.Combine(_dir, $"f{f:D3}.txt");
            // 10 处命中/文件 × 约 260 字节,1200 处远超 200 上限 → 触发地图
            await File.WriteAllLinesAsync(path,
                Enumerable.Range(1, 10).Select(i => "needle " + new string('y', 250) + i), TestContext.Current.CancellationToken);
        }

        string result = await _tools.Grep("needle", ct: TestContext.Current.CancellationToken);

        // 120 个文件全在搜索根下 → 逐层上收为 ".": 1200；没有文件级行号列表
        Assert.Contains("1200 处命中，跨 120 个文件", result);
        Assert.Contains("命中过多，下面是按目录的命中分布", result);
        Assert.Contains(".: 1200 处 / 120 个文件", result);
        Assert.Contains("已按目录聚合为 1 组", result);
        Assert.DoesNotContain("（行 ", result);
    }

    /// <summary>文件数在上限内时保持文件级地图（带行号），不升目录</summary>
    [Fact]
    public async Task Grep_Map_WithinFileLimit_StaysFileLevel()
    {
        for (int f = 0; f < 40; f++)
        {
            string path = Path.Combine(_dir, $"g{f:D2}.txt");
            await File.WriteAllLinesAsync(path,
                Enumerable.Range(1, 10).Select(i => "needle " + new string('y', 250) + i), TestContext.Current.CancellationToken);
        }

        string result = await _tools.Grep("needle", ct: TestContext.Current.CancellationToken);

        string[] mapLines = result.Split('\n').Where(line => line.Contains(" 处（行 ")).ToArray();
        Assert.Equal(40, mapLines.Length);
        Assert.Contains("400 处命中，跨 40 个文件", result);
        Assert.Contains("可按行号 `Read` 上面的文件", result);
        Assert.DoesNotContain("已按目录聚合", result);
    }

    /// <summary>一级目录仍超上限时逐层上收到父目录（120 子目录分 3 父 → 3 组）</summary>
    [Fact]
    public async Task Grep_Map_Hierarchy_GoesUpALevel()
    {
        for (int p = 0; p < 3; p++)
        {
            string parent = Path.Combine(_dir, $"dir{(char)('A' + p)}");
            for (int s = 0; s < 40; s++)
            {
                Directory.CreateDirectory(Path.Combine(parent, $"sub{s:D2}"));
                string path = Path.Combine(parent, $"sub{s:D2}", "f.txt");
                await File.WriteAllLinesAsync(path,
                    Enumerable.Range(1, 10).Select(i => "needle " + new string('y', 250) + i), TestContext.Current.CancellationToken);
            }
        }

        string result = await _tools.Grep("needle", ct: TestContext.Current.CancellationToken);

        // 一级 120 个子目录超限 → 上收到 3 个父目录，各 400 命中
        string[] dirLines = result.Split('\n').Where(line => line.Contains(": 400")).ToArray();
        Assert.Equal(3, dirLines.Length);
        Assert.Contains("dirA: 400", result);
        Assert.Contains("dirB: 400", result);
        Assert.Contains("dirC: 400", result);
        Assert.Contains("dirA: 400 处 / 40 个文件", result);
        Assert.Contains("已按目录聚合为 3 组", result);
        Assert.DoesNotContain("（行 ", result);
    }

    /// <summary>地图模式的 notice 不能丢正则降级说明（命中过多时模型关键依赖它判断"少了几条"的原因）</summary>
    [Fact]
    public async Task Grep_Map_KeepsFallbackAndNormalizationNotes()
    {
        for (int f = 0; f < 20; f++)
        {
            string path = Path.Combine(_dir, $"n{f:D2}.txt");
            // 20 文件 × 15 命中 = 300 处，超 200 上限触发地图（文件数 20 ≤ 100 保持文件级）
            await File.WriteAllLinesAsync(path,
                Enumerable.Range(1, 15).Select(i => "needle " + i), TestContext.Current.CancellationToken);
        }

        // 正则降级（backreference 不被支持）与 * 开头的归一化都要带到地图 notice 里
        string fellBack = await _tools.Grep(@"needle\1", ct: TestContext.Current.CancellationToken);
        Assert.Contains("已按字面串重搜", fellBack);

        string normalized = await _tools.Grep("*needle", ct: TestContext.Current.CancellationToken);
        Assert.Contains("模式已归一化为", normalized);
    }

    /// <summary>过宽的目录级地图输出必须压进 token 预算量级（≤ 50 组，不应出现数百行路径）</summary>
    [Fact]
    public async Task Grep_Map_StaysWithinOutputBudget()
    {
        for (int f = 0; f < 130; f++)
        {
            string path = Path.Combine(_dir, $"b{f:D3}.txt");
            await File.WriteAllLinesAsync(path,
                Enumerable.Range(1, 10).Select(i => "needle " + new string('z', 250) + i), TestContext.Current.CancellationToken);
        }

        string result = await _tools.Grep("needle", ct: TestContext.Current.CancellationToken);

        // 130 个文件超过文件级上限 → 目录级；输出应远小于正文预算 32KB（组数 ≤ 50，一行约几十字节）
        Assert.Contains("已按目录聚合", result);
        Assert.True(Encoding.UTF8.GetByteCount(result) < GrepResultShaper.MaxOutputBytes,
            $"地图输出超出预算：{Encoding.UTF8.GetByteCount(result)} bytes");
        Assert.True(Encoding.UTF8.GetByteCount(result) < 8000,
            $"地图输出偏大：{Encoding.UTF8.GetByteCount(result)} bytes");
    }

    /// <summary>文件级地图渲完仍超字节预算（超长路径）时降级到目录级，不硬塞超预算正文</summary>
    [Fact]
    public async Task Grep_FileLevelMap_OverByteBudget_FallsBackToDirectoryLevel()
    {
        // 100 个文件全在同一 Deep 目录（路径约 320+ 字符）→ 文件级 100 行逼近/超过 32KB → 降级
        var deep = Path.Combine(_dir, new string('d', 40), new string('d', 40), new string('d', 40),
            new string('d', 40), new string('d', 40), new string('d', 40), new string('d', 40));
        Directory.CreateDirectory(deep);
        for (int f = 0; f < 100; f++)
        {
            string path = Path.Combine(deep, $"f{f:D3}_long_name_to_inflate_path.txt");
            await File.WriteAllLinesAsync(path,
                Enumerable.Range(1, 10).Select(i => "needle " + i), TestContext.Current.CancellationToken);
        }

        string result = await _tools.Grep("needle", ct: TestContext.Current.CancellationToken);

        // 1000 命中 > 200 → 地图；文件数 100 ≤ 100 走文件级，但超预算 → 目录级 1 组
        Assert.Contains("已按目录聚合为 1 组", result);
        Assert.DoesNotContain("处命中（第", result);
        Assert.True(Encoding.UTF8.GetByteCount(result) < GrepResultShaper.MaxOutputBytes,
            $"降级后仍超预算：{Encoding.UTF8.GetByteCount(result)} bytes");
    }

    [Fact]
    public void TruncateLine_RespectsBudget()
    {
        Assert.Equal("short", ToolOutputTruncation.TruncateLine("short", 10));
        string truncated = ToolOutputTruncation.TruncateLine(new string('a', 100), 10);
        Assert.StartsWith("aaaaaaaaaa …[已截断]", truncated);
    }
}
