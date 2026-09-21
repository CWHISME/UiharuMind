/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.Core.SimpleLog;

/// <summary>
/// 磁盘格式与各项阈值。格式是<b>纯文本</b>：一行头 + 原样正文，条目间空一行。
///
/// 定位<b>不靠解析、不靠长度前缀、不靠分隔符扫描</b>——写之前记下当前偏移量直接存进索引项。
/// 正文里就算出现一模一样的头行，也只影响肉眼阅读，不影响程序定位。
/// <para>选纯文本而非 SQLite 的理由见 <c>docs/adr/0023</c>。</para>
/// </summary>
public static class LogFormat
{
    /// <summary>超过这个字符数的正文外置到 Bodies.txt，主流里只留摘要与引用</summary>
    public const int SpillThreshold = 4 * 1024;

    /// <summary>
    /// 首行预览的最大字符数。它是<b>常驻内存</b>的，因此这个数要乘以索引条数——
    /// 加长的同时必须看住 <c>LogStore.MaxIndexEntries</c>。
    /// 取 400 是为了在宽屏上也能铺满一行：截得比可视宽度短的话，列表行会莫名其妙地断在半路
    /// </summary>
    public const int PreviewLength = 400;

    /// <summary>主流单文件上限</summary>
    public const long MainMaxBytes = 8L * 1024 * 1024;

    /// <summary>主流保留代数，含当前代</summary>
    public const int MainGenerations = 10;

    /// <summary>外置正文单文件上限</summary>
    public const long BodiesMaxBytes = 64L * 1024 * 1024;

    /// <summary>外置正文保留代数，含当前代</summary>
    public const int BodiesGenerations = 10;

    /// <summary>启动时清理多少天前的日志文件</summary>
    public const int RetentionDays = 7;

    /// <summary>主流文件名主干</summary>
    public const string MainBaseName = "Log";

    /// <summary>外置正文文件名主干</summary>
    public const string BodiesBaseName = "Bodies";

    /// <summary>
    /// 条目的头行，形如 <c>[2026-09-13 17:01:02][Warning][LlmRequest] (12,345 chars)</c>
    /// </summary>
    /// <param name="item">日志条目</param>
    /// <returns>不带换行的头行</returns>
    public static string Header(LogItem item) =>
        $"[{item.Time:yyyy-MM-dd HH:mm:ss}][{item.LogType}][{item.Category}] ({item.Text.Length:N0} chars)";

    /// <summary>
    /// 取首行预览。列表行只显示一行，因此多行正文只取第一行。
    /// 后面还有内容时补一个省略号——不补的话，正好截在可视宽度上的那条看着像是完整的
    /// </summary>
    /// <param name="text">正文</param>
    /// <returns>不含换行、长度受限的预览</returns>
    public static string Preview(string text)
    {
        int end = text.IndexOfAny(['\r', '\n']);
        ReadOnlySpan<char> firstLine = end < 0 ? text : text.AsSpan(0, end);
        if (firstLine.Length <= PreviewLength) return end < 0 ? firstLine.ToString() : firstLine.ToString() + '…';
        return string.Concat(firstLine[..PreviewLength], "…");
    }
}
