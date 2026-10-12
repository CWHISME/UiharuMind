using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// Read 的附注：截断给总行数、负数 offset 读末尾、空文件与越界分开说、目录与二进制不硬读、
/// 找不到时列出工作区里的同名文件
/// </summary>
public sealed class ReadToolTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uiharu-read-").FullName;
    private readonly PermissiveFileAccessTools _tools;

    public ReadToolTests()
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

    private string Save(string relative, string text) => Save(relative, System.Text.Encoding.UTF8.GetBytes(text));

    private string Save(string relative, byte[] bytes)
    {
        string path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private Task<string> Read(string path, int offset = 1, int? limit = null) =>
        _tools.Read(path, offset, limit, cancellationToken: TestContext.Current.CancellationToken);

    private static string Lines(int count) => string.Join('\n', Enumerable.Range(1, count).Select(i => $"line{i}")) + "\n";

    [Fact]
    public async Task TruncationNotice_GivesTheTotalLineCount()
    {
        string path = Save("big.txt", Lines(2500));

        string result = await Read(path, limit: 100);

        Assert.EndsWith("…[已截断：显示第 1–100 行，共 2500 行；续读传 offset=101]", result);
        Assert.DoesNotContain("Summary", result); //Summary 没挂就不提
    }

    /// <summary>Summary 挂着时，截断附注后指一条去处：整体理解交给它，不一页页翻</summary>
    [Fact]
    public async Task TruncationNotice_PointsToSummary_WhenMounted()
    {
        string path = Save("big.txt", Lines(2500));
        var tools = new PermissiveFileAccessTools(new AgentPathResolver(_dir), offerSummary: true);

        string result = await tools.Read(path, 1, 100, cancellationToken: TestContext.Current.CancellationToken);

        Assert.EndsWith($"续读传 offset=101]\n要整体理解这份文件，可交给 `{BrainTool.ToolName}`，task 里写清要什么。", result);
        Assert.DoesNotContain(BrainTool.ToolName, await tools.Read(Save("small.txt", Lines(3)), 1, null,
            cancellationToken: TestContext.Current.CancellationToken)); //没截断不提
    }

    /// <summary>全文窗按结果推首行行号：截断附注为准（负 offset 也对），没截断用正 offset</summary>
    [Fact]
    public async Task FirstLineOf_MatchesTheFileLineNumbers()
    {
        string path = Save("big.txt", Lines(200));

        Assert.Equal(60, ReadTruncationNote.FirstLineOf(60, await Read(path, offset: 60, limit: 40)));
        Assert.Equal(151, ReadTruncationNote.FirstLineOf(-50, await Read(path, offset: -50, limit: 10)));
        Assert.Equal(190, ReadTruncationNote.FirstLineOf(190, await Read(path, offset: 190)));
        Assert.Equal(1, ReadTruncationNote.FirstLineOf(0, await Read(path, offset: 0, limit: 5)));
    }

    [Fact]
    public async Task NegativeOffset_ReadsTheTail()
    {
        string path = Save("log.txt", Lines(10));

        Assert.Equal("line8\nline9\nline10", await Read(path, offset: -3));
        Assert.StartsWith("line1\n", await Read(path, offset: -50)); //倒数超过总行数就从头读
    }

    [Fact]
    public async Task OffsetPastTheEnd_SaysHowLongTheFileIs()
    {
        string path = Save("short.txt", Lines(10));

        Assert.Equal($"offset 20 超出了 '{path}' 的末尾（共 10 行）。", await Read(path, offset: 20));
    }

    [Fact]
    public async Task EmptyFile_IsReportedAsEmpty()
    {
        string path = Save("empty.txt", string.Empty);

        Assert.Equal($"文件 '{path}' 是空的。", await Read(path));
    }

    [Fact]
    public async Task CrLfFile_IsShownWithPlainNewLines()
    {
        string path = Save("crlf.txt", "a\r\nb\r\nc");

        Assert.Equal("a\nb\nc", await Read(path));
    }

    [Fact]
    public async Task Directory_PointsToGlob()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "src"));

        string result = await Read("src");

        Assert.Contains("是目录", result);
        Assert.Contains($"`{FileToolNames.Glob}`", result);
    }

    /// <summary>读一次二进制从前就是 32KB 乱码进上下文</summary>
    [Fact]
    public async Task BinaryFile_IsNotDumpedAsText()
    {
        Save("lib.dll", [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0xFF, 0xFE]);

        string result = await Read("lib.dll");

        Assert.Equal("'lib.dll' 是二进制文件（8 B），无法按文本显示。", result);
    }

    /// <summary>实测找不到的大多是目录猜错了：同名文件就在工作区的别处</summary>
    [Fact]
    public async Task MissingFile_ListsFilesWithTheSameNameElsewhere()
    {
        Save("Code/Social/Public/Laws/Weight.h", "#pragma once\n");

        string result = await Read("Code/Laws/Public/Weight.h");

        Assert.StartsWith("找不到文件 'Code/Laws/Public/Weight.h'。", result);
        Assert.Contains("- Code/Social/Public/Laws/Weight.h", result);
    }

    [Fact]
    public async Task MissingFile_WithoutNamesakes_IsJustNotFound()
    {
        Assert.Equal("找不到文件 'nope.cs'。", await Read("nope.cs"));
    }

    [Fact]
    public async Task WildcardPath_PointsToGlob()
    {
        string result = await Read("issues/162-*.md");

        Assert.Contains($"`{FileToolNames.Glob}`", result);
    }
}
