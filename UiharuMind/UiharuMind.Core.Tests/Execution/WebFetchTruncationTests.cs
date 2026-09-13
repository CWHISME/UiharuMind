using System.Text;
using System.Text.RegularExpressions;
using UiharuMind.Core.AI.Execution.Tools.WebTools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉死 WebFetch 超限的截断形态:UTF-8 字节预算(与 Read/Grep 同口径)、
/// 头尾骨架带行号锚点、多字节字符不劈开、极长行退化只返头。
/// Format 是纯函数,落盘(save)与真实抓取不在这一层测。
/// </summary>
public class WebFetchTruncationTests
{
    private const string SavedPath = "/cache/FetchedPages/example_abc123.txt";

    [Fact]
    public void Format_UnderBudget_ReturnsTextUnchanged()
    {
        string text = "short page body";
        Assert.Equal(text, WebFetchTruncation.Format(text, SavedPath));
    }

    [Fact]
    public void Format_LongPage_ReturnsHeadTailWithLineAnchors()
    {
        // 约 110 字节/行 × 1200 行 ≈ 130KB:头约 60KB、尾约 4KB,中间留给 Read offset= 续读
        string[] lines = Enumerable.Range(1, 1200).Select(i => $"line {i:D4} " + new string('中', 35)).ToArray();
        string text = string.Join('\n', lines);

        string result = WebFetchTruncation.Format(text, SavedPath);

        Assert.Contains("[TAIL — ", result);
        Assert.Contains($"full content saved to {SavedPath}", result);
        Assert.Contains("continue with Read offset=", result);
        Assert.DoesNotContain("\uFFFD", result); //多字节字符没有被劈开

        // 行号锚点自洽:头说 lines 1–N,续读就是 offset=N+1
        Match m = Regex.Match(result, @"lines 1–(\d+).*?continue with Read offset=(\d+)");
        Assert.True(m.Success, $"应给出头行数与续读偏移,实际:{result}");
        Assert.Equal(int.Parse(m.Groups[1].Value) + 1, int.Parse(m.Groups[2].Value));

        // 头是原文前缀、尾是原文后缀(行对齐后)
        Assert.StartsWith("line 0001 ", result);
        Assert.EndsWith(lines[^1], result.TrimEnd());

        // 头尾各自在预算内
        string headPart = result[..result.IndexOf("\n\n---\n", StringComparison.Ordinal)];
        int tailMarker = result.LastIndexOf("[TAIL — ", StringComparison.Ordinal);
        string tailPart = result[(tailMarker + "[TAIL — ".Length)..];
        tailPart = tailPart[(tailPart.IndexOf('\n') + 1)..];
        Assert.True(Encoding.UTF8.GetByteCount(headPart) <= WebFetchTruncation.HeadBudgetBytes,
            $"头超预算:{Encoding.UTF8.GetByteCount(headPart)}");
        Assert.True(Encoding.UTF8.GetByteCount(tailPart) <= WebFetchTruncation.TailBudgetBytes,
            $"尾超预算:{Encoding.UTF8.GetByteCount(tailPart)}");
    }

    /// <summary>
    /// 单行占满全文(极长行):行号锚点不可靠,退化为只返头。
    /// 硬拼头尾会把同一行读两遍,还让模型以为中间有别的东西。
    /// </summary>
    [Fact]
    public void Format_SingleHugeLine_FallsBackToHeadOnly()
    {
        string text = new string('a', 100_000);

        string result = WebFetchTruncation.Format(text, SavedPath);

        Assert.Contains("tail omitted", result);
        Assert.DoesNotContain("[TAIL — ", result);
        Assert.StartsWith("aaaa", result);
        Assert.True(Encoding.UTF8.GetByteCount(result) <= WebFetchTruncation.MaxBytes,
            "退化返回仍应压在总量预算内");
    }

    [Fact]
    public void TakeHeadBytes_DoesNotSplitMultibyteChar()
    {
        // 每个汉字 3 字节;预算 100 不是 3 的倍数,应回退到 99 字节 = 33 个完整汉字
        string text = new string('中', 100);

        string head = WebFetchTruncation.TakeHeadBytes(text, 100);

        Assert.Equal(99, Encoding.UTF8.GetByteCount(head));
        Assert.DoesNotContain("\uFFFD", head);
    }

    [Fact]
    public void TakeTailBytes_DoesNotSplitMultibyteChar()
    {
        string text = new string('中', 100);

        string tail = WebFetchTruncation.TakeTailBytes(text, 100);

        Assert.Equal(99, Encoding.UTF8.GetByteCount(tail));
        Assert.DoesNotContain("\uFFFD", tail);
    }

    [Theory]
    [InlineData("https://example.com/docs/page")]
    [InlineData("http://localhost:8080/a?b=1&c=中文")]
    public void FileNameFor_IsStableAndSanitized(string url)
    {
        string name = WebFetchCacheSink.FileNameFor(url);

        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('\\', name);
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            Assert.DoesNotContain(invalid, name);
        }

        Assert.EndsWith(".txt", name);
        Assert.Equal(name, WebFetchCacheSink.FileNameFor(url)); //同一 URL 稳定映射,覆盖即缓存
    }

    [Fact]
    public void FileNameFor_DifferentUrls_Differ()
    {
        Assert.NotEqual(
            WebFetchCacheSink.FileNameFor("https://example.com/a"),
            WebFetchCacheSink.FileNameFor("https://example.com/b"));
    }
}
