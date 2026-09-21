/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// 文本文件的落盘保真信封：读入时把 BOM 摘下来记住，写回时原样戴回去。
///
/// 存在的理由是两处曾经的静默改写：
/// <list type="number">
/// <item><c>File.WriteAllTextAsync(..., Encoding.UTF8)</c> 会写出 UTF-8 preamble，而读取端
/// (<c>StreamReader</c> / <c>ReadAllTextAsync</c>) 又把 BOM 吃掉——于是每编辑一次，文件就长出一个 BOM。</item>
/// <item>编辑前把整份文件 CRLF 抹成 LF 再写回，会让 Windows 换行的文件整份变更。</item>
/// </list>
///
/// 因此本类<b>不归一正文</b>：<see cref="Text"/> 就是原文，一个字节都没动（只剥掉 BOM）。
/// 行尾差异交给按行匹配去吸收（见 <c>FileEditPlanner</c> 的保守 fuzzy），只有<b>新插入</b>的文本
/// 才按 <see cref="NewLine"/> 转写。未命中的行永远保持原始字节，混用换行的文件也不会被统一。
/// </summary>
internal readonly struct TextFileEnvelope
{
    private static readonly UTF8Encoding NoBomUtf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>原文件是否带 UTF-8 BOM</summary>
    public bool HasBom { get; }

    /// <summary>正文原样（已剥掉 BOM，行尾未做任何归一）</summary>
    public string Text { get; }

    /// <summary>新插入文本使用的行尾：取文件第一处换行的风格，无换行时为 <c>\n</c></summary>
    public string NewLine { get; }

    private TextFileEnvelope(bool hasBom, string text, string newLine)
    {
        HasBom = hasBom;
        Text = text;
        NewLine = newLine;
    }

    /// <summary>
    /// 从落盘字节解析信封
    /// </summary>
    /// <param name="bytes">文件字节</param>
    /// <returns>信封</returns>
    public static TextFileEnvelope FromBytes(ReadOnlySpan<byte> bytes)
    {
        bool hasBom = bytes.StartsWith(Utf8Bom);
        return FromText(NoBomUtf8.GetString(hasBom ? bytes[Utf8Bom.Length..] : bytes), hasBom);
    }

    /// <summary>
    /// 从内存中的文本构造信封（测试与预演用）
    /// </summary>
    /// <param name="text">正文</param>
    /// <param name="hasBom">落盘时是否带 BOM</param>
    /// <returns>信封</returns>
    public static TextFileEnvelope FromText(string text, bool hasBom = false)
    {
        int lf = text.IndexOf('\n');
        string newLine = lf > 0 && text[lf - 1] == '\r' ? "\r\n" : "\n";
        return new TextFileEnvelope(hasBom, text, newLine);
    }

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
    /// 把正文转成落盘字节：BOM 按读入时的原样，行尾一个字节都不动
    /// </summary>
    /// <param name="text">正文</param>
    /// <returns>落盘字节</returns>
    public byte[] ToBytes(string text)
    {
        if (!HasBom) return NoBomUtf8.GetBytes(text);

        byte[] payload = NoBomUtf8.GetBytes(text);
        byte[] result = new byte[Utf8Bom.Length + payload.Length];
        Utf8Bom.CopyTo(result.AsSpan());
        payload.CopyTo(result.AsSpan(Utf8Bom.Length));
        return result;
    }
}
