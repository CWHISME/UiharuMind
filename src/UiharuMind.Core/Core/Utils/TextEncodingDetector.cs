using System.Buffers;
using System.Text;
using System.Text.Unicode;
using UtfUnknown;

namespace UiharuMind.Core.Core.Utils;

/// <summary>编码探测的结论</summary>
public enum ETextEncodingVerdict
{
    /// <summary>认出了编码</summary>
    Text,

    /// <summary>二进制：开头有 NUL 字节，又没有宽字符 BOM 能解释它</summary>
    Binary,

    /// <summary>不是 UTF-8，也认不出别的编码</summary>
    Unknown,
}

/// <summary>
/// 一份字节的编码探测结果
/// </summary>
/// <param name="Verdict">结论</param>
/// <param name="Encoding">编码；结论不是 <see cref="ETextEncodingVerdict.Text"/> 时为 null</param>
/// <param name="BomLength">开头 BOM 的字节数，没有为 0</param>
public readonly record struct TextEncodingProbe(ETextEncodingVerdict Verdict, Encoding? Encoding, int BomLength)
{
    private const int Utf8CodePage = 65001;

    /// <summary>开头带 BOM</summary>
    public bool HasBom => BomLength > 0;

    /// <summary>是 UTF-8（纯 ASCII 也算）</summary>
    public bool IsUtf8 => Encoding?.CodePage == Utf8CodePage;

    /// <summary>
    /// 按探测出的编码解码，BOM 不进正文；二进制与认不出的按 UTF-8 解，坏字节成 U+FFFD
    /// </summary>
    /// <param name="bytes">探测时的那份字节</param>
    /// <returns>正文</returns>
    public string Decode(ReadOnlySpan<byte> bytes) =>
        (Encoding ?? TextEncodingDetector.Utf8NoBom).GetString(bytes[BomLength..]);
}

/// <summary>
/// 文本编码探测的唯一一份口径：知识库、文本编辑窗、文件工具的读改写、Grep 的转码都经这里。
/// 从前它们各按各的规则解码，同一个 GBK 文件在 Read 里是乱码，经 Edit 写回时全文中文被毁。
///
/// 判定顺序，后几步的位置都是被实测误判推出来的：
/// <list type="number">
/// <item>BOM 一锤定音。</item>
/// <item>开头 8KB 有 NUL 即二进制（带 BOM 的 UTF-16/32 上一步已认走）。</item>
/// <item>严格的 UTF-8 校验通过就是 UTF-8，纯 ASCII 也归这里。探测器排在它后面：几乎全是 ASCII、
/// 只夹一个中文字的 UTF-8 源码，探测器的最佳猜测是置信 0.52 的希腊文（ISO-8859-7）。</item>
/// <item>大体是 UTF-8、只混了零星坏字节（拼接来的文件、截断的字符）仍判 UTF-8：换成别的编码会让
/// 全文的中文变乱码，比几个替换字符糟得多。GBK 等按 UTF-8 解时绝大多数字节都是坏的，不会误入。</item>
/// <item>探测器给出多字节编码（GB18030、Big5、Shift-JIS、EUC 等）且置信 ≥0.5 才采纳。</item>
/// <item>东亚编码打分兜底。ASCII 在这几种编码里字节相同、没有区分度，不计入分母，
/// 否则中文稀疏的 GBK 源码永远到不了门槛。</item>
/// <item>探测器的单字节猜测（windows-1252、KOI8-R 等）置信 ≥0.5 才采纳，否则判认不出。</item>
/// </list>
/// GBK 按 GB18030 处理：它是 GBK 的超集，还能编码全部 Unicode，写回不会丢字。
/// </summary>
public static class TextEncodingDetector
{
    private const float MinimumConfidence = 0.50f;
    private const double MinimumEastAsianScore = 0.5;
    private const int BinaryProbeBytes = 8 * 1024; //开头这么多字节里有 NUL 即判二进制，与 git 同口径
    private const int MostlyUtf8Ratio = 4; //合法多字节字符至少是坏字节的这么多倍，才算大体是 UTF-8
    private static readonly int[] EastAsianCodePages = [54936, 950, 932]; //GB18030、Big5、Shift-JIS

    private const string CommonChineseCharacters =
        "的一是不了在人有我他这中大来上个国到说们为子和你地出道也时年得就那要下以生会自着去之过家学对可里后小么心多天而能好都然没日于起还发成事只作当想看文无开手十用主行方又如前所本见经头面公同三已老从动两长知民样现分将外但身些与高意进把法此实回二理美点月明其种声全工己话儿者向情部正名定女问力机给等几很业最间新什打便位因重被走电四第门相次东政海口使教西再平真听世气信少关并内加化由却代军产入先山五太水万市眼体别处总才场师书比住员九笑性通目华报立马命张活难神数件安表原车白应路期叫死常提感金何更反合放做系计或司利受光王果亲界及今京务强六像完德队据论则任形确吃场常";

    private const double MaximumControlCharacterRatio = 0.01d;

    internal static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly (byte[] Bom, Encoding Encoding)[] Boms =
    [
        ([0xEF, 0xBB, 0xBF], new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)),
        // UTF-32LE 的 BOM 以 UTF-16LE 的开头，必须先比
        ([0xFF, 0xFE, 0x00, 0x00], new UTF32Encoding(bigEndian: false, byteOrderMark: true)),
        ([0x00, 0x00, 0xFE, 0xFF], new UTF32Encoding(bigEndian: true, byteOrderMark: true)),
        ([0xFF, 0xFE], new UnicodeEncoding(bigEndian: false, byteOrderMark: true)),
        ([0xFE, 0xFF], new UnicodeEncoding(bigEndian: true, byteOrderMark: true)),
    ];

    static TextEncodingDetector()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// 探测一份字节的编码
    /// </summary>
    /// <param name="bytes">整个文件，或大文件开头的一段</param>
    /// <param name="isComplete">是不是整个文件。开头一段可能截在多字节字符中间，末尾那半个字符不算坏字节</param>
    /// <returns>探测结果</returns>
    public static TextEncodingProbe Detect(ReadOnlySpan<byte> bytes, bool isComplete = true)
    {
        foreach ((byte[] bom, Encoding encoding) in Boms)
        {
            if (bytes.StartsWith(bom)) return new TextEncodingProbe(ETextEncodingVerdict.Text, encoding, bom.Length);
        }

        if (bytes[..Math.Min(bytes.Length, BinaryProbeBytes)].Contains((byte)0))
        {
            return new TextEncodingProbe(ETextEncodingVerdict.Binary, null, 0);
        }

        if (IsValidUtf8(bytes, isComplete) || IsMostlyUtf8(bytes))
        {
            return new TextEncodingProbe(ETextEncodingVerdict.Text, Utf8NoBom, 0);
        }

        // 走到这里的已是少数（非 UTF-8 的文本），拷一份给探测器不算浪费
        DetectionDetail? detected = CharsetDetector.DetectFromBytes(bytes.ToArray()).Detected;
        bool confident = detected?.Encoding != null && detected.Confidence >= MinimumConfidence;
        if (confident && !detected!.Encoding.IsSingleByte)
        {
            return new TextEncodingProbe(ETextEncodingVerdict.Text, detected.Encoding, 0);
        }

        if (DetectEastAsian(bytes, isComplete) is { } eastAsian)
        {
            return new TextEncodingProbe(ETextEncodingVerdict.Text, eastAsian, 0);
        }

        return confident
            ? new TextEncodingProbe(ETextEncodingVerdict.Text, detected!.Encoding, 0)
            : new TextEncodingProbe(ETextEncodingVerdict.Unknown, null, 0);
    }

    /// <summary>
    /// 把非 UTF-8 的文本转成 UTF-8 字节：Grep 引擎的转码钩子，GBK、UTF-16 文件因此能按 UTF-8 搜
    /// </summary>
    /// <param name="bytes">文件字节</param>
    /// <returns>转好的字节；本来就是 UTF-8、是二进制或认不出编码时为 null（引擎照旧搜原字节）</returns>
    public static byte[]? TranscodeToUtf8(ReadOnlySpan<byte> bytes)
    {
        TextEncodingProbe probe = Detect(bytes);
        return probe.Verdict == ETextEncodingVerdict.Text && !probe.IsUtf8
            ? Utf8NoBom.GetBytes(probe.Decode(bytes))
            : null;
    }

    /// <summary>粗判「看着像纯文本」：按控制字符占比，拦住二进制文件</summary>
    /// <param name="text">解码后的正文</param>
    /// <returns>像纯文本为 true</returns>
    public static bool LooksLikePlainText(string text)
    {
        if (text.IndexOf('\0') >= 0) return false;
        if (text.Length == 0) return true;

        int controlCharacters = 0;
        foreach (char character in text)
        {
            if (IsStrayControl(character)) controlCharacters++;
        }

        return (double)controlCharacters / text.Length <= MaximumControlCharacterRatio;
    }

    private static bool IsValidUtf8(ReadOnlySpan<byte> bytes, bool isComplete)
    {
        if (Utf8.IsValid(bytes)) return true;
        if (isComplete) return false;

        return Rune.DecodeLastFromUtf8(bytes, out _, out int tail) == OperationStatus.NeedMoreData
               && Utf8.IsValid(bytes[..^tail]);
    }

    // 按 UTF-8 解出来的多字节字符远多于坏字节。GBK、Big5 的字节对只有一成左右碰巧是合法 UTF-8
    private static bool IsMostlyUtf8(ReadOnlySpan<byte> bytes)
    {
        int replaced = 0;
        int multiByte = 0;
        foreach (char character in Utf8NoBom.GetString(bytes))
        {
            if (character == '�') replaced++;
            else if (character > 0x7F) multiByte++;
        }

        return multiByte >= MostlyUtf8Ratio * replaced;
    }

    private static Encoding? DetectEastAsian(ReadOnlySpan<byte> bytes, bool isComplete)
    {
        (Encoding Encoding, double Score)? best = null;
        foreach (int codePage in EastAsianCodePages)
        {
            Encoding strict = Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
            if (TryDecodeStrict(strict, bytes, isComplete) is not { } text) continue;

            double score = Score(text, codePage);
            if (best == null || score > best.Value.Score) best = (Encoding.GetEncoding(codePage), score);
        }

        return best is { Score: > MinimumEastAsianScore } ? best.Value.Encoding : null;
    }

    // 不完整的开头一段不 flush：截在末尾的半个字符留在解码器里，不当坏字节
    private static string? TryDecodeStrict(Encoding strict, ReadOnlySpan<byte> bytes, bool isComplete)
    {
        try
        {
            char[] chars = new char[strict.GetMaxCharCount(bytes.Length)];
            int count = strict.GetDecoder().GetChars(bytes, chars, flush: isComplete);
            return new string(chars, 0, count);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static double Score(string text, int codePage)
    {
        if (!LooksLikePlainText(text)) return double.MinValue;

        double score = 0;
        int counted = 0;
        foreach (char character in text)
        {
            if (character <= 0x7F) continue;

            counted++;
            if (character is >= '一' and <= '鿿')
            {
                score += CommonChineseCharacters.Contains(character) ? 3 : 0.25;
            }
            else if (character is >= '぀' and <= 'ヿ') //假名
            {
                score += codePage == 932 ? 2.5 : -3;
            }
            else if (character is >= '｡' and <= 'ﾟ') //半角片假名
            {
                score += codePage == 932 ? 0.5 : -2;
            }
            else if (character is >= '　' and <= '〿' or >= '！' and <= '～') //全角标点
            {
                score += 0.5;
            }
            else if (!char.IsLetterOrDigit(character))
            {
                score -= 0.5;
            }
        }

        return counted == 0 ? 0 : score / counted;
    }

    private static bool IsStrayControl(char character) =>
        char.IsControl(character) && character is not '\r' and not '\n' and not '\t' and not '\f';
}
