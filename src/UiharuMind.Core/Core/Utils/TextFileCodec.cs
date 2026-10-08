/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;

namespace UiharuMind.Core.Core.Utils;

/// <summary>整文件读取结果</summary>
public sealed record TextFileReadResult(
    bool Success,
    string? Text,
    Encoding? Encoding,
    bool HasBom,
    string ErrorCode = "",
    string ErrorDetail = "");

/// <summary>
/// 磁盘文本文件的整读整写，知识库的 <c>PlainTextFileSourceReader</c> 与文本文件编辑窗（<c>TextFileWindow</c>）共用。
///
/// 读：编码交给 <see cref="TextEncodingDetector"/>（与文件工具同一份口径），再按控制字符占比判定是不是纯文本。
///     返回 <see cref="Encoding"/> 与是否带 BOM，写回时就能按原编码、原 BOM 落盘——
///     GBK/UTF-16 文件不会因为编辑一次就被改写成 UTF-8。
/// 写：只负责字节排布（编码 + 可选 BOM），行尾不做任何归一，编辑输入的内容原文写回。
/// </summary>
public static class TextFileCodec
{
    /// <summary>
    /// 读一个文本文件：探测编码 → 解码 → 纯文本校验。
    /// 首个 await 带 <c>ConfigureAwait(false)</c>，之后的探测与解码落在线程池上，不会卡 UI 线程。
    /// </summary>
    /// <param name="filePath">文件绝对路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>读取结果；失败时 <see cref="TextFileReadResult.ErrorCode"/> 为
    /// <c>FileMissing</c> / <c>EncodingUnknown</c> / <c>NotPlainText</c> / <c>ReadFailed</c></returns>
    public static async Task<TextFileReadResult> ReadTextAsync(
        string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return new TextFileReadResult(false, null, null, false, "FileMissing", filePath ?? "");

        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);

            TextEncodingProbe probe = TextEncodingDetector.Detect(bytes);
            if (probe.Verdict == ETextEncodingVerdict.Binary)
                return new TextFileReadResult(false, null, null, false, "NotPlainText");
            if (probe.Verdict == ETextEncodingVerdict.Unknown)
                return new TextFileReadResult(false, null, null, false, "EncodingUnknown");

            string text = probe.Decode(bytes);
            if (!TextEncodingDetector.LooksLikePlainText(text))
                return new TextFileReadResult(false, null, null, false, "NotPlainText");

            return new TextFileReadResult(true, text, probe.Encoding, probe.HasBom);
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
    /// 正文能不能用这个编码写下。UTF 系与 GB18030 能写任何字符；Big5、Shift-JIS 与单字节编码写不下的字符
    /// 会被默认的替换策略静默写成 <c>?</c>，所以写之前要先问
    /// </summary>
    /// <param name="encoding">编码</param>
    /// <param name="text">正文</param>
    /// <returns>写得下为 true</returns>
    public static bool CanEncode(Encoding encoding, string text)
    {
        if (encoding is UTF8Encoding or UnicodeEncoding or UTF32Encoding) return true;

        Encoding strict = (Encoding)encoding.Clone();
        strict.EncoderFallback = EncoderFallback.ExceptionFallback;
        try
        {
            strict.GetByteCount(text);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// 写一个文本文件：按原编码落盘；原文件带 BOM 时原样戴回。
    /// 编码写不下的字会被替换成 <c>?</c>，调用方先用 <see cref="CanEncode"/> 问过。
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
}
