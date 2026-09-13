using System.Text;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

/// <summary>
/// WebFetch 超限时的截断与返回文案。纯函数,便于单测。
///
/// 口径与 Read/Grep 一致:按 <b>UTF-8 字节</b>算,而不是字符——中文一个字符三字节,
/// 按字符算会放进三四倍 token(Read 曾因此改过口径,见 PermissiveFileAccessTools)。
///
/// 超限形态是「头 + 分隔 Notice + 尾」:
/// - 头:正文开头,文章/文档最相关的部分;
/// - 尾:很多网页的结论/更新说明在末尾,给一小段尾部,模型拿到头尾骨架通常不用 Read 就能判断这页值不值得深读;
/// - Notice:行号锚点 + 落盘路径,需要中部时按 <c>Read offset=</c> 续读,不必从头重灌。
/// </summary>
internal static class WebFetchTruncation
{
    /// <summary>单次返回的总量预算(UTF-8 字节)。头+尾预算之和等于它</summary>
    public const int MaxBytes = 64 * 1024;

    /// <summary>头预算:保留正文开头</summary>
    public const int HeadBudgetBytes = 60 * 1024;

    /// <summary>尾预算:给一小段尾部,省一次 Read</summary>
    public const int TailBudgetBytes = 4 * 1024;

    /// <summary>
    /// 把超限正文切成「头 + 分隔 Notice + 尾」的返回形态。
    /// 头尾按行对齐(行号锚点才诚实);整行装不进预算的极长行退化为按字节截断的部分行;
    /// 头尾重叠/相邻(全文被极长行主导)时退化为只返头,避免同一行出现两遍。
    /// </summary>
    /// <param name="text">超限正文(调用方已确认 UTF-8 字节数 &gt; <see cref="MaxBytes"/>)</param>
    /// <param name="savedPath">落盘全文的路径</param>
    /// <returns>给模型的最终返回文本</returns>
    public static string Format(string text, string savedPath)
    {
        long totalBytes = Encoding.UTF8.GetByteCount(text);
        if (totalBytes <= MaxBytes) return text; //防御:调用方只在超限时调用,误用也不会走进退化分支

        string[] lines = text.Split('\n');
        // 末尾 '\n' 会产出一个空元素,它不算一行(对齐 Read 的 ReadLine 语义)
        int totalLines = lines.Length;
        if (lines[^1].Length == 0) totalLines--;

        // 头:尽量装下完整行,装不下第一行(极长行)就按字节截部分行
        int headLines = 0;
        int headBytes = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length == 0 && i == lines.Length - 1) continue; //末尾 '\n' 的空元素不算行
            int lineBytes = Encoding.UTF8.GetByteCount(line) + 1;
            if (headBytes + lineBytes > HeadBudgetBytes) break;
            headBytes += lineBytes;
            headLines++;
        }

        string head = headLines > 0
            ? string.Join('\n', lines.Take(headLines)) + "\n"
            : TakeHeadBytes(lines[0], HeadBudgetBytes);

        // 尾:从末尾往回装完整行,装不下最后一行(极长行)就按字节截部分行
        int tailLines = 0;
        int tailBytes = 0;
        for (int i = lines.Length - 1; i >= 0 && tailLines < totalLines; i--)
        {
            string line = lines[i];
            if (line.Length == 0 && i == lines.Length - 1) continue; //末尾 '\n' 的空元素不算行
            int lineBytes = Encoding.UTF8.GetByteCount(line) + 1;
            if (tailBytes + lineBytes > TailBudgetBytes) break;
            tailBytes += lineBytes;
            tailLines++;
        }

        string tail = tailLines > 0
            ? string.Join('\n', lines.Skip(lines.Length - tailLines))
            : TakeTailBytes(lines[^1], TailBudgetBytes);
        // tailLines == 0 时尾部是「最后一行的一部分」,起始行就是最后一行,不是 totalLines+1
        int tailStartLine = tailLines == 0 ? totalLines : totalLines - tailLines + 1;

        string headRegion = headLines == 0 ? "part of line 1" : headLines == 1 ? "line 1" : $"lines 1–{headLines}";
        string tailRegion = tailLines == 0
            ? $"part of line {totalLines}"
            : tailStartLine == totalLines
                ? $"line {totalLines}"
                : $"lines {tailStartLine}–{totalLines}";

        // 头尾相邻或重叠:不再有可续读的中间地带,退化为只返头
        if (tailStartLine <= headLines + 1)
        {
            return $"{head}\n\n---\n"
                   + $"*[Truncated — showing first {Encoding.UTF8.GetByteCount(head)} bytes ({headRegion}) of {totalBytes} bytes / {totalLines} lines; "
                   + $"full content saved to {savedPath}; head and tail overlap (very long lines), tail omitted]*";
        }

        return $"{head}\n\n---\n"
               + $"*[Truncated — showing first {Encoding.UTF8.GetByteCount(head)} bytes ({headRegion}) and last {Encoding.UTF8.GetByteCount(tail)} bytes ({tailRegion}) of {totalBytes} bytes / {totalLines} lines; "
               + $"full content saved to {savedPath}; continue with Read offset={headLines + 1}]*\n\n---\n\n"
               + $"[TAIL — {tailRegion}]\n{tail}";
    }

    /// <summary>取前 budget 字节,不劈开多字节字符</summary>
    internal static string TakeHeadBytes(string text, int budgetBytes)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length <= budgetBytes) return text;
        int end = budgetBytes;
        while (end > 0 && (bytes[end] & 0xC0) == 0x80) end--; //回退到字符边界
        return Encoding.UTF8.GetString(bytes, 0, end);
    }

    /// <summary>取后 budget 字节,不劈开多字节字符</summary>
    internal static string TakeTailBytes(string text, int budgetBytes)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length <= budgetBytes) return text;
        int start = bytes.Length - budgetBytes;
        while (start < bytes.Length && (bytes[start] & 0xC0) == 0x80) start++; //前进到字符边界
        return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
    }
}
