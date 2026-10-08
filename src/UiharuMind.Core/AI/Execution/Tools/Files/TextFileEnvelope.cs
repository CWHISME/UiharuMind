/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// 文本文件的落盘保真信封：读入时记住编码与 BOM，写回时原样还原。
///
/// 存在的理由是几处曾经的静默改写：
/// <list type="number">
/// <item><c>File.WriteAllTextAsync(..., Encoding.UTF8)</c> 会写出 UTF-8 preamble，而读取端
/// (<c>StreamReader</c> / <c>ReadAllTextAsync</c>) 又把 BOM 吃掉——于是每编辑一次，文件就长出一个 BOM。</item>
/// <item>编辑前把整份文件 CRLF 抹成 LF 再写回，会让 Windows 换行的文件整份变更。</item>
/// <item>一律按 UTF-8 解码再写回：GBK 文件只改一行 ASCII，全文中文就成了「锟斤拷」。</item>
/// </list>
///
/// 因此本类<b>不归一正文</b>：<see cref="Text"/> 就是原文按原编码解出来的样子（只剥掉 BOM）。
/// 行尾差异交给按行匹配去吸收（见 <c>FileEditPlanner</c> 的保守 fuzzy），只有<b>新插入</b>的文本
/// 才按 <see cref="NewLine"/> 转写。未命中的行永远保持原始字节，混用换行的文件也不会被统一。
/// 编码探测与知识库、文本编辑窗同一份口径（<see cref="TextEncodingDetector"/>）。
/// </summary>
internal readonly struct TextFileEnvelope
{
    private static readonly UTF8Encoding NoBomUtf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly UTF8Encoding BomUtf8 = new(encoderShouldEmitUTF8Identifier: true);

    private readonly Encoding? _encoding; //default 实例（失败的编辑计划）没有编码

    private TextFileEnvelope(Encoding encoding, bool hasBom, string text, string newLine, bool isLossless)
    {
        _encoding = encoding;
        HasBom = hasBom;
        Text = text;
        NewLine = newLine;
        IsLossless = isLossless;
    }

    /// <summary>落盘编码：读入时探测出来的；新建文件与认不出编码的文件为 UTF-8</summary>
    public Encoding Encoding => _encoding ?? NoBomUtf8;

    /// <summary>原文件是否带 BOM</summary>
    public bool HasBom { get; }

    /// <summary>正文原样（已剥掉 BOM，行尾未做任何归一）</summary>
    public string Text { get; }

    /// <summary>新插入文本使用的行尾：取文件第一处换行的风格，无换行时为 <c>\n</c></summary>
    public string NewLine { get; }

    /// <summary>
    /// 原字节能否按 <see cref="Encoding"/> 原样还原。编码认不出、或文件里混着在该编码下无效的字节时为 false：
    /// 这时改一处再写回，没碰到的部分也会被改写
    /// </summary>
    public bool IsLossless { get; }

    /// <summary>编码名，进给模型的话术（<c>gb18030</c>、<c>utf-16</c>、<c>shift_jis</c>…）</summary>
    public string EncodingName => Encoding.WebName;

    /// <summary>
    /// 从落盘字节解析信封
    /// </summary>
    /// <param name="bytes">文件字节</param>
    /// <returns>信封</returns>
    public static TextFileEnvelope FromBytes(ReadOnlySpan<byte> bytes)
    {
        TextEncodingProbe probe = TextEncodingDetector.Detect(bytes);
        string text = probe.Decode(bytes);
        if (probe.Verdict != ETextEncodingVerdict.Text) return Create(NoBomUtf8, false, text, isLossless: false);

        // 往返一次才知道有没有坏字节：解码时它们已悄悄成了替换字符
        bool lossless = probe.Encoding!.GetBytes(text).AsSpan().SequenceEqual(bytes[probe.BomLength..]);
        return Create(probe.Encoding, probe.HasBom, text, lossless);
    }

    /// <summary>
    /// 从内存中的文本构造信封（测试与预演用），编码为 UTF-8
    /// </summary>
    /// <param name="text">正文</param>
    /// <param name="hasBom">落盘时是否带 BOM</param>
    /// <returns>信封</returns>
    public static TextFileEnvelope FromText(string text, bool hasBom = false) =>
        Create(hasBom ? BomUtf8 : NoBomUtf8, hasBom, text, isLossless: true);

    /// <summary>
    /// 换成无 BOM 的 UTF-8，行尾风格不变（新正文原编码写不下时用）
    /// </summary>
    /// <returns>新信封</returns>
    public TextFileEnvelope ToUtf8() => new(NoBomUtf8, false, Text, NewLine, isLossless: true);

    /// <summary>
    /// 把模型给的文本（一律按 <c>\n</c> 写）转成本文件的行尾风格
    /// </summary>
    /// <param name="text">模型给的文本</param>
    /// <returns>转写后的文本</returns>
    public string ConvertNewLines(string text)
    {
        string lf = text.Replace("\r\n", "\n");
        return NewLine == "\n" ? lf : lf.Replace("\n", NewLine);
    }

    /// <summary>
    /// 正文能不能用本文件的编码写下（口径见 <see cref="TextFileCodec.CanEncode"/>）
    /// </summary>
    /// <param name="text">正文</param>
    /// <returns>写得下为 true</returns>
    public bool CanEncode(string text) => TextFileCodec.CanEncode(Encoding, text);

    /// <summary>
    /// 把正文转成落盘字节：编码与 BOM 按读入时的原样，行尾一个字节都不动
    /// </summary>
    /// <param name="text">正文</param>
    /// <returns>落盘字节</returns>
    public byte[] ToBytes(string text)
    {
        byte[] payload = Encoding.GetBytes(text);
        if (!HasBom) return payload;

        ReadOnlySpan<byte> preamble = Encoding.Preamble;
        byte[] result = new byte[preamble.Length + payload.Length];
        preamble.CopyTo(result);
        payload.CopyTo(result.AsSpan(preamble.Length));
        return result;
    }

    private static TextFileEnvelope Create(Encoding encoding, bool hasBom, string text, bool isLossless)
    {
        int lf = text.IndexOf('\n');
        string newLine = lf > 0 && text[lf - 1] == '\r' ? "\r\n" : "\n";
        return new TextFileEnvelope(encoding, hasBom, text, newLine, isLossless);
    }
}
