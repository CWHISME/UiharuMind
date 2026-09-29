using System.Text;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>
/// 工具输出超限时的截断与返回文案（WebFetch、McpCall 等共用）。纯函数,便于单测。
///
/// 口径与 Read/Grep 一致:按 <b>UTF-8 字节</b>算,而不是字符——中文一个字符三字节,
/// 按字符算会放进三四倍 token(Read 曾因此改过口径,见 PermissiveFileAccessTools)。
///
/// 超限形态是「头 + 分隔 Notice + 尾」:
/// - 头:正文开头,文章/文档最相关的部分;
/// - 尾:很多网页的结论/更新说明在末尾,给一小段尾部,模型拿到头尾骨架通常不用 Read 就能判断这页值不值得深读;
/// - Notice:行号锚点 + 落盘路径,需要中部时按 <c>Read offset=</c> 续读,不必从头重灌。
/// </summary>
internal static class ToolOutputTruncation
{
    /// <summary>单次返回的总量预算(UTF-8 字节)。头+尾预算之和等于它（WebFetch 用的那一档）</summary>
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
    public static string Format(string text, string savedPath) => Format(text, savedPath, ToolOutputBudget.Page);

    /// <summary>
    /// 同上，预算由调用方给。
    /// </summary>
    /// <param name="text">超限正文</param>
    /// <param name="savedPath">落盘全文的路径</param>
    /// <param name="budget">总量、头、尾三档预算</param>
    /// <returns>给模型的最终返回文本</returns>
    public static string Format(string text, string savedPath, ToolOutputBudget budget)
    {
        long totalBytes = Encoding.UTF8.GetByteCount(text);
        if (totalBytes <= budget.MaxBytes) return text; //防御:调用方只在超限时调用,误用也不会走进退化分支

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
            if (headBytes + lineBytes > budget.HeadBytes) break;
            headBytes += lineBytes;
            headLines++;
        }

        string head = headLines > 0
            ? string.Join('\n', lines.Take(headLines)) + "\n"
            : TakeHeadBytes(lines[0], budget.HeadBytes);

        // 尾:从末尾往回装完整行,装不下最后一行(极长行)就按字节截部分行
        int tailLines = 0;
        int tailBytes = 0;
        for (int i = lines.Length - 1; i >= 0 && tailLines < totalLines; i--)
        {
            string line = lines[i];
            if (line.Length == 0 && i == lines.Length - 1) continue; //末尾 '\n' 的空元素不算行
            int lineBytes = Encoding.UTF8.GetByteCount(line) + 1;
            if (tailBytes + lineBytes > budget.TailBytes) break;
            tailBytes += lineBytes;
            tailLines++;
        }

        string tail = tailLines > 0
            ? string.Join('\n', lines.Skip(lines.Length - tailLines))
            : TakeTailBytes(lines[^1], budget.TailBytes);
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

/// <summary>
/// 一次工具输出的体量预算：总量、头、尾（UTF-8 字节）。
///
/// 分两档是因为两类输出的<b>寿命</b>不同：网页读一次就够，MCP 结果（日志、场景数据）
/// 却常常又大又频繁，而每一条都会进历史、之后每一轮都随请求重发，直到被压缩。
/// 同样 60KB，在网页上是「读一篇」，在 MCP 上是「往后每一轮都多背一万五千 token」。
/// </summary>
/// <param name="MaxBytes">总量预算，超过即落盘截断</param>
/// <param name="HeadBytes">保留的开头</param>
/// <param name="TailBytes">保留的结尾</param>
internal readonly record struct ToolOutputBudget(int MaxBytes, int HeadBytes, int TailBytes)
{
    /// <summary>网页正文（WebFetch）：64KB，头 60KB + 尾 4KB</summary>
    public static readonly ToolOutputBudget Page = new(64 * 1024, 60 * 1024, 4 * 1024);

    /// <summary>
    /// MCP 结果：8KB（约两千 token），头 4KB + 尾 4KB，对称。
    /// 取 8KB 是用户的经验值：MCP 结果一般用不到更多，甚至嫌多；要看全文有落盘的文件可按行号续读。
    /// 对称是因为关键信息在哪头取决于工具：日志最新的在末尾，测试结果的汇总在末尾，
    /// 搜索结果、场景层级、报错信息却多半在开头——没有哪一头值得多给，缺的部分让模型去读落盘文件。
    /// </summary>
    public static readonly ToolOutputBudget Compact = new(8 * 1024, 4 * 1024, 4 * 1024);

    /// <summary>
    /// McpHelp 的工具清单：32KB，头 24KB + 尾 8KB（约能容纳一百八十个工具）。
    /// 清单是模型<b>发现</b>工具的唯一入口——被截断时丢掉的恰恰是中间那批，它们从此无从发现。
    /// 实测 Unity MCP 全开的 73 个工具约 12.8KB，按 <see cref="Compact"/> 的 8KB 会有近三十个消失。
    /// 一次性读一遍、之后基本不再重发（模型记得住），所以放宽的代价远小于日志类结果。
    /// </summary>
    public static readonly ToolOutputBudget Catalog = new(32 * 1024, 24 * 1024, 8 * 1024);
}
