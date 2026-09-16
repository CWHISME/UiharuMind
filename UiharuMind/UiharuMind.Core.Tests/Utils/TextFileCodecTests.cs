using System.Text;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Tests.Utils;

/// <summary>
/// 文本文件编解码：保编码、保 BOM、拦二进制。
///
/// 这段逻辑是知识库读文件与界面编辑窗共用的，最该钉住的是三件静默改数据的坏事：
/// 编辑一次文件长出一个 BOM、GBK 文件被改写成 UTF-8、以及二进制文件被当纯文本读进来。
/// </summary>
public class TextFileCodecTests
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    private static string NewTempPath()
    {
        return Path.Combine(Path.GetTempPath(), $"textfile-codec-{Guid.NewGuid():N}.tmp");
    }

    /// <summary>UTF-8 带 BOM：读能识别 BOM、写回还带 BOM，正文一字不差</summary>
    [Fact]
    public async Task Utf8WithBom_RoundtripsWithBomPreserved()
    {
        string path = NewTempPath();
        try
        {
            // GetBytes 不输出 preamble，BOM 要手拼——这正是被测逻辑要防的坑
            byte[] original = [0xEF, 0xBB, 0xBF, .. Utf8WithBom.GetBytes("line one\nline two\n")];
            await File.WriteAllBytesAsync(path, original);

            TextFileReadResult read = await TextFileCodec.ReadTextAsync(path, default);

            Assert.True(read.Success);
            Assert.True(read.HasBom);
            Assert.Equal("line one\nline two\n", read.Text);

            await TextFileCodec.WriteTextAsync(path, "line one\nEDITED\n", read.Encoding!, read.HasBom, default);
            byte[] written = await File.ReadAllBytesAsync(path);

            Assert.True(written.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.Equal("line one\nEDITED\n",
                Encoding.UTF8.GetString(written, 3, written.Length - 3));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>无 BOM 纯文本：读不应虚报 BOM，写回也不该长出 BOM</summary>
    [Fact]
    public async Task PlainUtf8WithoutBom_WriteBackAddsNoBom()
    {
        string path = NewTempPath();
        try
        {
            await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("hello"));

            TextFileReadResult read = await TextFileCodec.ReadTextAsync(path, default);

            Assert.True(read.Success);
            Assert.False(read.HasBom);
            Assert.Equal("hello", read.Text);

            await TextFileCodec.WriteTextAsync(path, "hello world", read.Encoding!, read.HasBom, default);
            byte[] written = await File.ReadAllBytesAsync(path);

            Assert.Equal("hello world", Encoding.UTF8.GetString(written));
            Assert.False(written.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>GB18030（无 BOM）：要么探测出来，要么东亚兜底；正文必须原样，不许乱码</summary>
    [Fact]
    public async Task Gb18030Chinese_TextSurvivesRoundtrip()
    {
        string path = NewTempPath();
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Encoding gb = Encoding.GetEncoding(54936);
            string original = "第一行内容\n第二行内容\n";
            await File.WriteAllBytesAsync(path, gb.GetBytes(original));

            TextFileReadResult read = await TextFileCodec.ReadTextAsync(path, default);

            Assert.True(read.Success);
            Assert.Equal(original, read.Text);

            // 写回后读回来仍是同一份字节（编码 + 无 BOM 都不变）
            await TextFileCodec.WriteTextAsync(path, original, read.Encoding!, read.HasBom, default);
            Assert.Equal(gb.GetBytes(original), await File.ReadAllBytesAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>二进制文件不许被当纯文本读：带 NUL 或高控制字符占比都要拒</summary>
    [Fact]
    public async Task BinaryFile_IsRejectedAsNotPlainText()
    {
        string path = NewTempPath();
        try
        {
            byte[] binary = { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0x00, 0x10 };
            await File.WriteAllBytesAsync(path, binary);

            TextFileReadResult read = await TextFileCodec.ReadTextAsync(path, default);

            Assert.False(read.Success);
            Assert.Equal("NotPlainText", read.ErrorCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>文件不存在要报 FileMissing，而不是内部异常</summary>
    [Fact]
    public async Task MissingFile_ReturnsFileMissing()
    {
        string path = Path.Combine(Path.GetTempPath(), $"textfile-codec-{Guid.NewGuid():N}.never-exists");

        TextFileReadResult read = await TextFileCodec.ReadTextAsync(path, default);

        Assert.False(read.Success);
        Assert.Equal("FileMissing", read.ErrorCode);
    }

    /// <summary>空文件算纯文本（真文本里确实可能有一字没有的文件）</summary>
    [Fact]
    public void LooksLikePlainText_EmptyIsFineNullByteIsNot()
    {
        Assert.True(TextFileCodec.LooksLikePlainText(string.Empty));
        Assert.True(TextFileCodec.LooksLikePlainText("第一行\nsecond line\n"));
        Assert.False(TextFileCodec.LooksLikePlainText("ab\u0000cd"));
    }
}