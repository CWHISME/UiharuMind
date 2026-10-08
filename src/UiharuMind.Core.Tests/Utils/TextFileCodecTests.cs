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
            await File.WriteAllBytesAsync(path, original, TestContext.Current.CancellationToken);

            TextFileReadResult read = await TextFileCodec.ReadTextAsync(path, TestContext.Current.CancellationToken);

            Assert.True(read.Success);
            Assert.True(read.HasBom);
            Assert.Equal("line one\nline two\n", read.Text);

            await TextFileCodec.WriteTextAsync(path, "line one\nEDITED\n", read.Encoding!, read.HasBom, TestContext.Current.CancellationToken);
            byte[] written = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);

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
            await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("hello"), TestContext.Current.CancellationToken);

            TextFileReadResult read = await TextFileCodec.ReadTextAsync(path, TestContext.Current.CancellationToken);

            Assert.True(read.Success);
            Assert.False(read.HasBom);
            Assert.Equal("hello", read.Text);

            await TextFileCodec.WriteTextAsync(path, "hello world", read.Encoding!, read.HasBom, TestContext.Current.CancellationToken);
            byte[] written = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);

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
            await File.WriteAllBytesAsync(path, gb.GetBytes(original), TestContext.Current.CancellationToken);

            TextFileReadResult read = await TextFileCodec.ReadTextAsync(path, TestContext.Current.CancellationToken);

            Assert.True(read.Success);
            Assert.Equal(original, read.Text);

            // 写回后读回来仍是同一份字节（编码 + 无 BOM 都不变）
            await TextFileCodec.WriteTextAsync(path, original, read.Encoding!, read.HasBom, TestContext.Current.CancellationToken);
            Assert.Equal(gb.GetBytes(original), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
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
            await File.WriteAllBytesAsync(path, binary, TestContext.Current.CancellationToken);

            TextFileReadResult read = await TextFileCodec.ReadTextAsync(path, TestContext.Current.CancellationToken);

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

        TextFileReadResult read = await TextFileCodec.ReadTextAsync(path, TestContext.Current.CancellationToken);

        Assert.False(read.Success);
        Assert.Equal("FileMissing", read.ErrorCode);
    }

    /// <summary>
    /// 编辑窗保存前靠它决定要不要问「改存 UTF-8」：Shift-JIS 存不下 emoji、Big5 存不下简体、
    /// windows-1252 存不下中文，不问就会被静默写成「?」
    /// </summary>
    [Theory]
    [InlineData("shift_jis", "日本語😀", false)]
    [InlineData("big5", "繁體简体", false)]
    [InlineData("windows-1252", "café 中文", false)]
    [InlineData("windows-1252", "café", true)]
    [InlineData("gb18030", "简体😀", true)]
    [InlineData("utf-8", "任何😀字符", true)]
    public void CanEncode_SaysWhetherTheTextSurvivesTheEncoding(string encodingName, string text, bool expected)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        Assert.Equal(expected, TextFileCodec.CanEncode(Encoding.GetEncoding(encodingName), text));
    }

    /// <summary>
    /// 纯 ASCII 文件按 UTF-8 记：从前记成 us-ascii，在编辑窗里打几个中文再保存就成了「??」
    /// </summary>
    [Fact]
    public async Task AsciiFile_IsReadAsUtf8_SoChineseTypedLaterSurvivesTheSave()
    {
        string path = NewTempPath();
        try
        {
            await File.WriteAllBytesAsync(path, "int a = 1;\n"u8.ToArray(), TestContext.Current.CancellationToken);

            TextFileReadResult read = await TextFileCodec.ReadTextAsync(path, TestContext.Current.CancellationToken);
            await TextFileCodec.WriteTextAsync(path, "int a = 1; // 中文\n", read.Encoding!, read.HasBom,
                TestContext.Current.CancellationToken);

            Assert.Equal("int a = 1; // 中文\n", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }
}