using System.Text;
using Microsoft.Agents.AI;
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
    /// 实际放进三四倍于标称的 token。行数远未到顶时,字节上限必须先生效并给出续读点。
    /// </summary>
    [Fact]
    public async Task Read_ChineseFile_IsCappedByBytesNotChars()
    {
        string path = Path.Combine(_dir, "chinese.md");
        // 每行 100 个汉字 = 300 字节,300 行约 90KB,行数只有 300 远未到 2000
        await File.WriteAllLinesAsync(path,
            Enumerable.Range(1, 300).Select(_ => new string('测', 100)));

        string result = await _tools.Read(path);

        string[] lines = result.Split('\n');
        Assert.Contains("continue with offset=", lines[^1]);
        Assert.True(lines.Length - 1 < 300, $"应因字节上限提前截断,实际返回 {lines.Length - 1} 行");
        Assert.True(Encoding.UTF8.GetByteCount(result) < PermissiveFileAccessTools.MaxReadTotalBytes * 2,
            "截断后总字节应在上限量级内");
    }

    [Fact]
    public async Task Grep_Matches_AreCappedWithSentinel()
    {
        string path = Path.Combine(_dir, "haystack.txt");
        await File.WriteAllLinesAsync(path, Enumerable.Range(1, 300).Select(i => $"needle {i}"));

        List<FileSearchResult> results = await _tools.Grep("needle");

        Assert.Equal(PermissiveFileAccessTools.MaxGrepMatches + 1, results.Count); //上限 + 哨兵条目
        Assert.Equal("[truncated]", results[^1].FileName);
        Assert.Contains("Narrow the query", results[^1].Snippet);
    }

    [Fact]
    public void TruncateLine_RespectsBudget()
    {
        Assert.Equal("short", PermissiveFileAccessTools.TruncateLine("short", 10));
        string truncated = PermissiveFileAccessTools.TruncateLine(new string('a', 100), 10);
        Assert.StartsWith("aaaaaaaaaa …[truncated]", truncated);
    }
}
