using UiharuMind.Core.AI.Execution.Files;

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
        _tools.Read(path, offset, limit, TestContext.Current.CancellationToken);

    private static string Lines(int count) => string.Join('\n', Enumerable.Range(1, count).Select(i => $"line{i}")) + "\n";

    [Fact]
    public async Task TruncationNotice_GivesTheTotalLineCount()
    {
        string path = Save("big.txt", Lines(2500));

        string result = await Read(path, limit: 100);

        Assert.EndsWith("…[truncated: showing lines 1–100 of 2500; continue with offset=101]", result);
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

        Assert.Equal($"offset 20 is past the end of '{path}' (10 lines).", await Read(path, offset: 20));
    }

    [Fact]
    public async Task EmptyFile_IsReportedAsEmpty()
    {
        string path = Save("empty.txt", string.Empty);

        Assert.Equal($"File '{path}' is empty.", await Read(path));
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

        Assert.Contains("is a directory", result);
        Assert.Contains($"`{FileToolNames.Glob}`", result);
    }

    /// <summary>读一次二进制从前就是 32KB 乱码进上下文</summary>
    [Fact]
    public async Task BinaryFile_IsNotDumpedAsText()
    {
        Save("lib.dll", [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0xFF, 0xFE]);

        string result = await Read("lib.dll");

        Assert.Equal("'lib.dll' is a binary file (8 B); its content can't be shown as text.", result);
    }

    /// <summary>实测找不到的大多是目录猜错了：同名文件就在工作区的别处</summary>
    [Fact]
    public async Task MissingFile_ListsFilesWithTheSameNameElsewhere()
    {
        Save("Code/Social/Public/Laws/Weight.h", "#pragma once\n");

        string result = await Read("Code/Laws/Public/Weight.h");

        Assert.StartsWith("File 'Code/Laws/Public/Weight.h' not found.", result);
        Assert.Contains("- Code/Social/Public/Laws/Weight.h", result);
    }

    [Fact]
    public async Task MissingFile_WithoutNamesakes_IsJustNotFound()
    {
        Assert.Equal("File 'nope.cs' not found.", await Read("nope.cs"));
    }

    [Fact]
    public async Task WildcardPath_PointsToGlob()
    {
        string result = await Read("issues/162-*.md");

        Assert.Contains($"`{FileToolNames.Glob}`", result);
    }
}
