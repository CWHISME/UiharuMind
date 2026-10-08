using System.Text;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Tests.Utils;

/// <summary>
/// 编码探测的判定顺序。每一行样本都对应一次实测误判：旧链路把只夹一个中文字的 UTF-8 源码判成希腊文、
/// 把稀疏中文的 GBK 判成西里尔文或干脆认不出、把 Big5 判成 windows-1252
/// </summary>
public class TextEncodingDetectorTests
{
    private const string Code = "using System;\nnamespace A;\npublic class Foo\n{\n    public int Bar() => 42;\n}\n";

    static TextEncodingDetectorTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static TheoryData<string, string, string> Samples => new()
    {
        { "utf-8", Code, "utf-8" },
        { "utf-8", Code.Replace("public int", "// 中\n    public int"), "utf-8" },
        { "utf-8", Code.Replace("public int", "// 返回默认值\n    public int"), "utf-8" },
        { "utf-8", "// 日本語のコメントです。\n" + Code, "utf-8" },
        { "gb18030", Code.Replace("public int", "// 中\n    public int"), "gb18030" },
        { "gb18030", Code.Replace("public int", "// 返回默认值\n    public int"), "gb18030" },
        { "gb18030", string.Concat(Enumerable.Repeat("// 这是一段比较长的中文注释，说明下面这个方法的用途。\n" + Code, 20)), "gb18030" },
        { "gb18030", "你好", "gb18030" },
        { "big5", Code.Replace("public int", "// 返回預設值\n    public int"), "big5" },
        { "shift_jis", "// 日本語のコメントです。ファイルを読み込みます。\n" + Code, "shift_jis" },
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void Samples_AreDetectedAsTheirRealEncoding(string writtenAs, string text, string expected)
    {
        byte[] bytes = Encoding.GetEncoding(writtenAs).GetBytes(text);

        TextEncodingProbe probe = TextEncodingDetector.Detect(bytes);

        Assert.Equal(ETextEncodingVerdict.Text, probe.Verdict);
        Assert.Equal(Encoding.GetEncoding(expected).CodePage, probe.Encoding!.CodePage);
        Assert.Equal(text, probe.Decode(bytes));
    }

    /// <summary>开头一段截在多字节字符中间：末尾那半个字符不算坏字节，仍是 UTF-8</summary>
    [Fact]
    public void SampleCutInsideACharacter_IsStillUtf8()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("前缀 " + new string('x', 100) + "中文");

        TextEncodingProbe probe = TextEncodingDetector.Detect(bytes[..^1], isComplete: false);

        Assert.True(probe.IsUtf8);
    }

    /// <summary>大体是 UTF-8、混了一个坏字节：仍按 UTF-8 解，坏的那处成替换字符，别的中文不乱</summary>
    [Fact]
    public void Utf8WithAStrayByte_StaysUtf8()
    {
        byte[] bytes = [.. Encoding.UTF8.GetBytes("// 这里是一段正常的中文注释\n"), 0xFF, .. "int a;\n"u8];

        TextEncodingProbe probe = TextEncodingDetector.Detect(bytes);

        Assert.True(probe.IsUtf8);
        Assert.Contains("正常的中文注释", probe.Decode(bytes));
    }

    [Fact]
    public void Utf16WithBom_IsText_AndTheBomStaysOutOfTheText()
    {
        byte[] bytes = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("第一行\nline two\n")];

        TextEncodingProbe probe = TextEncodingDetector.Detect(bytes);

        Assert.Equal(ETextEncodingVerdict.Text, probe.Verdict);
        Assert.Equal(2, probe.BomLength);
        Assert.Equal("第一行\nline two\n", probe.Decode(bytes));
    }

    [Fact]
    public void NulInTheHead_IsBinary()
    {
        Assert.Equal(ETextEncodingVerdict.Binary, TextEncodingDetector.Detect([0x89, 0x50, 0x4E, 0x47, 0x00, 0x01]).Verdict);
    }

    [Fact]
    public void TranscodeToUtf8_ConvertsOtherEncodings_AndLeavesUtf8Alone()
    {
        byte[] gbk = Encoding.GetEncoding("gb18030").GetBytes("// 返回默认值\nint a;\n");

        Assert.Equal("// 返回默认值\nint a;\n", Encoding.UTF8.GetString(TextEncodingDetector.TranscodeToUtf8(gbk)!));
        Assert.Null(TextEncodingDetector.TranscodeToUtf8("plain ascii"u8));
    }

    /// <summary>空文件算纯文本（真文本里确实可能有一字没有的文件）</summary>
    [Fact]
    public void LooksLikePlainText_EmptyIsFineNullByteIsNot()
    {
        Assert.True(TextEncodingDetector.LooksLikePlainText(string.Empty));
        Assert.True(TextEncodingDetector.LooksLikePlainText("第一行\nsecond line\n"));
        Assert.False(TextEncodingDetector.LooksLikePlainText("ab\u0000cd"));
    }
}
