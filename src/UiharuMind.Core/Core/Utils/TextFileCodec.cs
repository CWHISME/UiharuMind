/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using UtfUnknown;

namespace UiharuMind.Core.Core.Utils;

/// <summary>
/// 文本文件的读与写（带编码探测），供「磁盘文本文件」这一类场景共用：
/// 知识库的 <c>PlainTextFileSourceReader</c> 与文本文件编辑窗（<c>TextFileWindow</c>）各持一份
/// 迟早会在编码行为上分岔（一边改了 BOM 处理、另一边没跟上），所以探测链只在这里有一份。
///
/// 读：BOM → charset-detector（置信度 ≥0.5）→ 东亚编码（GB18030/Big5/Shift-JIS）打分兜底，
///     再按控制字符占比判定是不是纯文本。返回 <see cref="Encoding"/> 与是否带 BOM，
///     写回时就能按原编码、原 BOM 落盘——GBK/UTF-16 文件不会因为编辑一次就被改写成 UTF-8。
/// 写：只负责字节排布（编码 + 可选 BOM），行尾不做任何归一，编辑输入的内容原文写回。
///
/// 注意：与 <see cref="UiharuMind.Core.AI.Execution.Files.TextFileEnvelope"/> 并存但各司其职——
/// 信封是「工具改文件」那条按行匹配模糊路径的 UTF-8 专属保真方案；这里供全文件整读写。
/// </summary>
/// <summary>整文件读取结果</summary>
public sealed record TextFileReadResult(
    bool Success,
    string? Text,
    Encoding? Encoding,
    bool HasBom,
    string ErrorCode = "",
    string ErrorDetail = "");

public static class TextFileCodec
{
    private const float MinimumConfidence = 0.50f;
    private const double MaximumControlCharacterRatio = 0.01d;

    private const string CommonChineseCharacters =
        "的一是不了在人有我他这中大来上个国到说们为子和你地出道也时年得就那要下以生会自着去之过家学对可里后小么心多天而能好都然没日于起还发成事只作当想看文无开手十用主行方又如前所本见经头面公同三已老从动两长知民样现分将外但身些与高意进把法此实回二理美点月明其种声全工己话儿者向情部正名定女问力机给等几很业最间新什打便位因重被走电四第门相次东政海口使教西再平真听世气信少关并内加化由却代军产入先山五太水万市眼体别处总才场师书比住员九笑性通目华报立马命张活难神数件安表原车白应路期叫死常提感金何更反合放做系计或司利受光王果亲界及今京务强六像完德队据论则任形确吃场常";

    static TextFileCodec()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// 读一个文本文件：探测编码 → 解码 → 纯文本校验
    ///
    /// 磁盘只读一次：先取全部字节，BOM 一锤定音（有 BOM 时编码由 BOM 决定，插件探测与东亚兜底
    /// 都不必跑）；无 BOM 才把同一份字节交给 <see cref="CharsetDetector.DetectFromBytes"/>，
    /// 不再像旧实现那样让插件自己再读一遍文件。首个 await 带
    /// <c>ConfigureAwait(false)</c>，后续的解码与探测落在线程池上，不会卡 UI 线程。
    /// </summary>
    /// <param name="filePath">文件绝对路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>读取结果；失败时 <see cref="TextFileReadResult.ErrorCode"/> 为
    /// <c>FileMissing</c> / <c>EncodingUnknown</c> / <c>NotPlainText</c></returns>
    public static async Task<TextFileReadResult> ReadTextAsync(
        string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return new TextFileReadResult(false, null, null, false, "FileMissing", filePath ?? "");

        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);

            Encoding? bom = DetectBomEncoding(bytes); //BOM 只是前缀比较，不算一次探测
            bool hasBom = bom != null;
            Encoding? encoding = bom ?? DetectByHeuristics(bytes);
            if (encoding == null)
                return new TextFileReadResult(false, null, null, false, "EncodingUnknown");

            string text = Decode(bytes, encoding);

            if (!LooksLikePlainText(text))
                return new TextFileReadResult(false, null, null, false, "NotPlainText");

            return new TextFileReadResult(true, text, encoding, hasBom);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            return new TextFileReadResult(false, null, null, false, "ReadFailed", e.Message);
        }
    }

    /// <summary>
    /// 无 BOM 时的内容探测：先让 charset-detector 基于已读入的同一份字节判断（不再读盘），
    /// 置信度 ≥0.5 采纳；否则东亚编码（GB18030/Big5/Shift-JIS）打分兜底
    /// </summary>
    private static Encoding? DetectByHeuristics(byte[] bytes)
    {
        DetectionDetail? detected = CharsetDetector.DetectFromBytes(bytes).Detected;
        if (detected?.Encoding != null && detected.Confidence >= MinimumConfidence)
            return detected.Encoding;

        return DetectEastAsianFallback(bytes);
    }

    /// <summary>
    /// 写一个文本文件：按原编码落盘；原文件带 BOM 时原样戴回。
    /// 行尾不归一——编辑输入什么就写什么。
    /// </summary>
    /// <param name="filePath">文件绝对路径</param>
    /// <param name="text">正文</param>
    /// <param name="encoding">读入时探测到的编码</param>
    /// <param name="preserveBom">原文件是否带 BOM</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async Task WriteTextAsync(
        string filePath, string text, Encoding encoding, bool preserveBom, CancellationToken cancellationToken)
    {
        byte[] body = encoding.GetBytes(text);
        byte[] result = body;
        if (preserveBom && encoding.Preamble.Length > 0)
        {
            ReadOnlySpan<byte> preamble = encoding.Preamble;
            result = new byte[preamble.Length + body.Length];
            preamble.CopyTo(result);
            body.CopyTo(result.AsSpan(preamble.Length));
        }

        await File.WriteAllBytesAsync(filePath, result, cancellationToken).ConfigureAwait(false);
    }

    private static Encoding? DetectBomEncoding(ReadOnlySpan<byte> bytes)
    {
        if (HasPrefix(bytes, 0xEF, 0xBB, 0xBF)) return new UTF8Encoding(true);
        if (HasPrefix(bytes, 0xFF, 0xFE, 0x00, 0x00)) return new UTF32Encoding(false, true);
        if (HasPrefix(bytes, 0x00, 0x00, 0xFE, 0xFF)) return new UTF32Encoding(true, true);
        if (HasPrefix(bytes, 0xFF, 0xFE)) return new UnicodeEncoding(false, true);
        if (HasPrefix(bytes, 0xFE, 0xFF)) return new UnicodeEncoding(true, true);
        return null;
    }

    private static bool HasPrefix(ReadOnlySpan<byte> bytes, params byte[] prefix)
    {
        return bytes.Length >= prefix.Length && bytes[..prefix.Length].SequenceEqual(prefix);
    }

    private static Encoding? DetectEastAsianFallback(byte[] bytes)
    {
        (Encoding Encoding, double Score)? best = null;
        foreach (int codePage in new[] { 54936, 950, 932 })
        {
            try
            {
                Encoding encoding = Encoding.GetEncoding(codePage,
                    EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                string text = Decode(bytes, encoding);
                double score = ScoreDecodedText(text, codePage);
                if (best == null || score > best.Value.Score) best = (encoding, score);
            }
            catch (DecoderFallbackException)
            {
            }
        }

        return best is { Score: > 0.5 } ? best.Value.Encoding : null;
    }

    private static string Decode(byte[] bytes, Encoding encoding)
    {
        int preambleLength = bytes.AsSpan().StartsWith(encoding.Preamble)
            ? encoding.Preamble.Length
            : 0;
        return encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
    }

    private static double ScoreDecodedText(string text, int codePage)
    {
        if (!LooksLikePlainText(text) || text.Length == 0) return double.MinValue;

        double score = 0;
        foreach (char character in text)
        {
            if (character <= 0x7F)
            {
                score += char.IsControl(character) ? -2 : 0.1;
                continue;
            }

            if (character is >= '\u4E00' and <= '\u9FFF')
            {
                score += CommonChineseCharacters.Contains(character) ? 3 : 0.25;
                continue;
            }

            bool kana = character is >= '\u3040' and <= '\u30FF';
            if (kana)
            {
                score += codePage == 932 ? 2.5 : -3;
                continue;
            }

            if (character is >= '\uFF61' and <= '\uFF9F')
            {
                score += codePage == 932 ? 0.5 : -2;
                continue;
            }

            score -= char.IsLetterOrDigit(character) ? 0 : 0.5;
        }

        return score / text.Length;
    }

    /// <summary>粗判「看着像纯文本」：按控制字符占比，拦住二进制文件</summary>
    public static bool LooksLikePlainText(string text)
    {
        if (text.IndexOf('\0') >= 0) return false;
        if (text.Length == 0) return true;

        int controlCharacters = 0;
        foreach (char character in text)
        {
            if (char.IsControl(character) && character is not '\r' and not '\n' and not '\t' and not '\f')
                controlCharacters++;
        }

        return (double)controlCharacters / text.Length <= MaximumControlCharacterRatio;
    }
}