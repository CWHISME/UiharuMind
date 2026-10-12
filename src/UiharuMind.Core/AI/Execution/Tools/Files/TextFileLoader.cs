using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>按探测出的编码解好的正文；二进制时 <see cref="Text"/> 为空串</summary>
/// <param name="Probe">编码探测结论</param>
/// <param name="Text">正文（已剥 BOM，行尾原样）</param>
/// <param name="ByteCount">原文件字节数</param>
internal readonly record struct DecodedText(TextEncodingProbe Probe, string Text, long ByteCount)
{
    /// <summary>是否二进制文件</summary>
    public bool IsBinary => Probe.Verdict == ETextEncodingVerdict.Binary;
}

/// <summary>
/// 文件工具读文本的唯一入口：读字节 → 探测编码 → 判二进制 → 解码。编码口径与知识库、Edit 同一份
/// （<see cref="TextEncodingDetector"/>），切行口径见 <see cref="TextLines"/>。
/// 大文件不整份进内存：只拿开头探测编码，再按流逐行读，行号与整读一致。
/// Edit 的落盘保真另走 <see cref="TextFileEnvelope"/>：它要拿原字节做往返校验，职责不同。
/// </summary>
internal static class TextFileLoader
{
    /// <summary>这么大以内整份读进来：编码看全文，顺手数出总行数；更大的按流读</summary>
    public const long WholeFileBytes = 8 * 1024 * 1024;

    private const int SampleBytes = 64 * 1024; //按流读的大文件只拿开头这么多探测编码

    /// <summary>
    /// 整份读入并解码
    /// </summary>
    /// <param name="path">文件绝对路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>解好的正文</returns>
    public static async Task<DecodedText> ReadAllAsync(string path, CancellationToken ct) =>
        Decode(await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false));

    /// <summary>
    /// 整份读入并解码（同步版）
    /// </summary>
    /// <param name="path">文件绝对路径</param>
    /// <returns>解好的正文</returns>
    public static DecodedText ReadAll(string path) => Decode(File.ReadAllBytes(path));

    /// <summary>
    /// 只拿开头一段探测编码（按流读的大文件用）
    /// </summary>
    /// <param name="path">文件绝对路径</param>
    /// <returns>探测结论，结论是「开头像什么」</returns>
    public static TextEncodingProbe ProbeHead(string path)
    {
        byte[] head = new byte[SampleBytes];
        int headLength;
        using (FileStream stream = File.OpenRead(path))
        {
            headLength = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        }

        return TextEncodingDetector.Detect(head.AsSpan(0, headLength), isComplete: false);
    }

    /// <summary>
    /// 按流逐行读，口径同 <see cref="TextLines"/>
    /// </summary>
    /// <param name="path">文件绝对路径</param>
    /// <param name="probe">开头探测出的编码</param>
    /// <returns>各行文本，惰性读取</returns>
    public static IEnumerable<string> StreamLines(string path, TextEncodingProbe probe)
    {
        using FileStream stream = File.OpenRead(path);
        stream.Position = probe.BomLength;
        using StreamReader reader = new(stream, probe.Encoding ?? TextEncodingDetector.Utf8NoBom,
            detectEncodingFromByteOrderMarks: false);
        while (reader.ReadLine() is { } line) yield return line;
    }

    /// <summary>
    /// 取全部行：小文件整读、大文件按流读
    /// </summary>
    /// <param name="path">文件绝对路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>探测结论与各行；二进制时行为 null</returns>
    public static async Task<(TextEncodingProbe Probe, IEnumerable<string>? Lines)> ReadLinesAsync(string path,
        CancellationToken ct)
    {
        if (new FileInfo(path).Length > WholeFileBytes)
        {
            TextEncodingProbe head = ProbeHead(path);
            return (head, head.Verdict == ETextEncodingVerdict.Binary ? null : StreamLines(path, head));
        }

        DecodedText decoded = await ReadAllAsync(path, ct).ConfigureAwait(false);
        return (decoded.Probe, decoded.IsBinary ? null : TextLines.Enumerate(decoded.Text));
    }

    private static DecodedText Decode(byte[] bytes)
    {
        TextEncodingProbe probe = TextEncodingDetector.Detect(bytes);
        string text = probe.Verdict == ETextEncodingVerdict.Binary ? string.Empty : probe.Decode(bytes);
        return new DecodedText(probe, text, bytes.Length);
    }
}
