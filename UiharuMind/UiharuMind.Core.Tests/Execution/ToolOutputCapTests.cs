using System.Text;
using UiharuMind.Core.AI.Execution.Files;

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
        await File.WriteAllLinesAsync(path, Enumerable.Range(1, 2500).Select(i => $"line{i}"));

        string result = await _tools.Read(path);

        string[] lines = result.Split('\n');
        Assert.Equal(PermissiveFileAccessTools.DefaultReadLineLimit + 1, lines.Length); //窗口 + 截断提示行
        Assert.Equal("line1", lines[0]);
        Assert.Contains($"offset={PermissiveFileAccessTools.DefaultReadLineLimit + 1}", lines[^1]);
    }

    [Fact]
    public async Task Read_Offset_ContinuesFromWhereTruncationPointed()
    {
        string path = Path.Combine(_dir, "big2.txt");
        await File.WriteAllLinesAsync(path, Enumerable.Range(1, 2500).Select(i => $"line{i}"));

        string result = await _tools.Read(path, offset: PermissiveFileAccessTools.DefaultReadLineLimit + 1);

        Assert.StartsWith($"line{PermissiveFileAccessTools.DefaultReadLineLimit + 1}", result);
        Assert.DoesNotContain("[truncated", result); //剩余 500 行在窗口内,不应再截断
    }

    [Fact]
    public async Task Read_OverlongLine_IsTruncatedInline()
    {
        string path = Path.Combine(_dir, "minified.js");
        await File.WriteAllTextAsync(path, new string('x', 50_000));

        string result = await _tools.Read(path);

        Assert.Contains("…[truncated]", result);
        Assert.True(result.Length < 3000, $"单行截断后总长应远小于原文,实际 {result.Length}");
    }

    /// <summary>
    /// 总量上限按 UTF-8 字节算,而不是字符——中文一个字符三字节,按字符算会让中文文件
    /// 实际放进三四倍于标称的 token。传 limit=4000 绕过行数限制,让 1MB 字节上限先生效。
    /// </summary>
    [Fact]
    public async Task Read_ChineseFile_IsCappedByBytesNotChars()
    {
        string path = Path.Combine(_dir, "chinese.md");
        // 每行 100 个汉字 = 301 字节,4000 行约 1.2MB,传 limit=4000 绕过行数限制
        await File.WriteAllLinesAsync(path,
            Enumerable.Range(1, 4000).Select(_ => new string('测', 100)));

        string result = await _tools.Read(path, limit: 4000);

        string[] lines = result.Split('\n');
        Assert.Contains("continue with offset=", lines[^1]);
        Assert.True(lines.Length - 1 < 4000, $"应因字节上限提前截断,实际返回 {lines.Length - 1} 行");
        Assert.True(Encoding.UTF8.GetByteCount(result) < PermissiveFileAccessTools.MaxReadTotalBytes * 2,
            "截断后总字节应在上限量级内");
    }

    /// <summary>
    /// limit=-1 是全文读模式:绕过 1MB 字节上限,读完整个文件。
    /// 用于需要完整理解文件以做重构的场景——分段读容易让模型忘了前面读了什么。
    /// </summary>
    [Fact]
    public async Task Read_WithLimitNegativeOne_ReadsEntireFile()
    {
        string path = Path.Combine(_dir, "full.txt");
        // 3000 行 × 约 500 字节/行 = 1.5MB,超过 1MB 字节上限
        await File.WriteAllLinesAsync(path,
            Enumerable.Range(1, 3000).Select(i => new string('x', 480) + i));

        string result = await _tools.Read(path, limit: -1);

        // 全文读绕过字节上限,不应有截断提示
        Assert.DoesNotContain("[truncated", result);
        Assert.DoesNotContain("continue with offset=", result);
        // 应包含最后一行
        Assert.Contains("3000", result);
        // 总字节应超过 1MB(证明绕过了字节上限)
        Assert.True(Encoding.UTF8.GetByteCount(result) > PermissiveFileAccessTools.MaxReadTotalBytes,
            "limit=-1 应绕过字节上限,实际返回字节应超过上限");
    }

    [Fact]
    public async Task Grep_Matches_AreCappedWithSentinel()
    {
        string path = Path.Combine(_dir, "haystack.txt");
        await File.WriteAllLinesAsync(path, Enumerable.Range(1, 300).Select(i => $"needle {i}"));

        GrepToolResult result = await _tools.Grep("needle");

        // 按文件分组:300 处命中来自同一文件 → 一组,但命中数仍封顶在 200
        GrepFileHits file = Assert.Single(result.Matches);
        Assert.Equal("haystack.txt", file.File);
        Assert.Equal(PermissiveFileAccessTools.MaxGrepMatches, file.Lines.Count);
        Assert.NotNull(result.Notice);
        Assert.Contains("100 more", result.Notice);
        Assert.Contains("Narrow the query", result.Notice);
    }

    /// <summary>
    /// contextLines 曾是个假参数:一路传进了引擎,转换时只取命中行,上下文全丢。
    /// 模型要了上下文却拿回孤零零一行,只能再发一次 Read——这条测试就是那个坑的看门人。
    /// </summary>
    [Fact]
    public async Task Grep_WithContextLines_ReturnsSurroundingLines()
    {
        string path = Path.Combine(_dir, "ctx.txt");
        await File.WriteAllLinesAsync(path, ["l1", "l2", "target", "l4", "l5"]);

        GrepToolResult result = await _tools.Grep("target", contextLines: 1);

        // 组内行保持 grep 味:命中行 "N:content",上下文行 "N-content"(对齐 ripgrep)
        GrepFileHits file = Assert.Single(result.Matches);
        Assert.Equal("ctx.txt", file.File);
        Assert.Equal(["2-l2", "3:target", "4-l4"], file.Lines);
    }

    /// <summary>同一文件的多处命中,重叠上下文行只出现一次;行号升序</summary>
    [Fact]
    public async Task Grep_AggregatesPerFile_AndDeduplicatesOverlappingContext()
    {
        string path = Path.Combine(_dir, "dense.txt");
        await File.WriteAllLinesAsync(path, ["hit", "hit", "hit"]);

        GrepToolResult result = await _tools.Grep("hit", contextLines: 2);

        GrepFileHits file = Assert.Single(result.Matches);
        Assert.Equal("dense.txt", file.File);
        // 三处命中+重叠上下文去重后,行号仍是 1/2/3,按行号升序
        Assert.Equal(3, file.Lines.Count);
        Assert.Equal([1, 2, 3], file.Lines.Select(ParseLineNumber).ToArray());

        static int ParseLineNumber(string s)
        {
            // 形如 "N:content"(命中)或 "N-content"(上下文),行号就是开头那串数字
            int end = s.IndexOfAny([':', '-']);
            return int.Parse(s[..end]);
        }
    }

    [Fact]
    public void TruncateLine_RespectsBudget()
    {
        Assert.Equal("short", PermissiveFileAccessTools.TruncateLine("short", 10));
        string truncated = PermissiveFileAccessTools.TruncateLine(new string('a', 100), 10);
        Assert.StartsWith("aaaaaaaaaa …[truncated]", truncated);
    }
}
