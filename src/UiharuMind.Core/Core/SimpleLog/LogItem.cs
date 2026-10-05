/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Buffers;
using System.Text;

namespace UiharuMind.Core.Core.SimpleLog;

/// <summary>
/// 一条日志的<b>全部内容</b>。正文不设上限——一条几 MB 的 LLM 请求正文也是一条完整条目。
///
/// 正文有两种形态：普通日志只有 <see cref="Text"/>；请求体这类大正文由调用方直接给 UTF-8 字节，
/// 拷进池里租的缓冲随条目入队，写线程落完盘即还池——整条正文从不经过字符串。
///
/// ⚠️ 不要与 <see cref="LogIndexEntry"/> 混淆：条目是<b>磁盘上</b>的东西，且只在写入途中
/// 短暂存在于内存；索引项才是常驻内存的那个。运行期内存里<b>没有</b>条目的完整正文。
/// </summary>
public sealed class LogItem
{
    private readonly bool _isUtf8; //正文是不是字节形态
    private byte[]? _utf8Body; //池里租的正文字节,落盘后还池
    private int _utf8Length;
    private string? _preview;

    /// <summary>级别</summary>
    public ELogType LogType { get; }

    /// <summary>内容性质</summary>
    public ELogCategory Category { get; }

    /// <summary>产生时刻</summary>
    public DateTime Time { get; }

    /// <summary>正文，不截断。带 UTF-8 正文的条目里，它只是排在字节正文之前的那段引导文字</summary>
    public string Text { get; }

    /// <summary>整条正文的字符数（引导文字 + UTF-8 正文），头行里报的就是它</summary>
    public int CharCount { get; }

    /// <summary>首行预览，列表行与控制台显示的就是它</summary>
    public string Preview => _preview ??= _isUtf8 ? LogFormat.Preview(Text, Utf8Body) : LogFormat.Preview(Text);

    internal ReadOnlySpan<byte> Utf8Body => _utf8Body.AsSpan(0, _utf8Length);

    /// <summary>整条正文编码成 UTF-8 后的字节数</summary>
    internal int Utf8ByteCount => Encoding.UTF8.GetByteCount(Text) + _utf8Length;

    public LogItem(ELogType type, string text, ELogCategory category = ELogCategory.General)
    {
        LogType = type;
        Category = category;
        Time = DateTime.Now;
        Text = text;
        CharCount = text.Length;
    }

    /// <summary>
    /// 带 UTF-8 正文的条目：正文拷进池里租的缓冲，调用方的那份用完即可复用
    /// </summary>
    /// <param name="type">级别</param>
    /// <param name="lead">排在正文之前的引导文字</param>
    /// <param name="utf8Body">UTF-8 正文</param>
    /// <param name="category">内容性质</param>
    internal LogItem(ELogType type, string lead, ReadOnlySpan<byte> utf8Body, ELogCategory category)
        : this(type, lead, category)
    {
        _isUtf8 = true;
        _utf8Body = ArrayPool<byte>.Shared.Rent(Math.Max(utf8Body.Length, 1));
        utf8Body.CopyTo(_utf8Body);
        _utf8Length = utf8Body.Length;
        CharCount = lead.Length + Encoding.UTF8.GetCharCount(utf8Body);
    }

    /// <summary>
    /// 把整条正文编码进目标缓冲
    /// </summary>
    /// <param name="destination">至少 <see cref="Utf8ByteCount"/> 字节</param>
    /// <returns>写入的字节数</returns>
    internal int CopyUtf8To(Span<byte> destination)
    {
        int written = Encoding.UTF8.GetBytes(Text, destination);
        Utf8Body.CopyTo(destination[written..]);
        return written + _utf8Length;
    }

    /// <summary>落盘后归还字节正文。之后正文只剩预览（已算好的会留着）</summary>
    internal void ReleaseBody()
    {
        if (_utf8Body == null) return;
        _ = Preview;
        ArrayPool<byte>.Shared.Return(_utf8Body);
        _utf8Body = null;
        _utf8Length = 0;
    }

    /// <summary>落盘与控制台共用的形态：头行 + 正文。字节正文只给预览，控制台不该吃下整份请求体</summary>
    public override string ToString() => LogFormat.Header(this) + Environment.NewLine + (_isUtf8 ? Preview : Text);
}
