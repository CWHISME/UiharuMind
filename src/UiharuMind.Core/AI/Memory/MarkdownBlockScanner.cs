using System.Text;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Memory;

/// <summary>块的类型。切块器据此决定能不能在块内部下刀</summary>
public enum EMemoryBlockKind
{
    /// <summary>普通段落。内部可以再切</summary>
    Paragraph,

    /// <summary>围栏代码块。内部切开就只剩半截代码,尽量整块走</summary>
    Code,

    /// <summary>表格。按行切尚可,但要带上表头行</summary>
    Table
}

/// <summary>
/// 一个结构块
/// </summary>
/// <param name="Kind">块类型</param>
/// <param name="Text">块正文(标题行不含在内)</param>
/// <param name="HeaderPath">所属标题路径,如「安装 / macOS」;不在任何标题下时为空串</param>
public readonly record struct MemoryTextBlock(EMemoryBlockKind Kind, string Text, string HeaderPath);

/// <summary>
/// 把 Markdown 文本扫成结构块序列。
///
/// 单独成类而不是塞进 <see cref="MemoryTextChunker"/>：识别结构和按预算打包是两件事,
/// 前者只认语法、后者只认 token,混在一起就成了一个谁都不敢改的大函数。
/// 两者都是纯函数,可以各自钉住行为。
///
/// 纯文本恰好是合法的 Markdown（没有标题、没有围栏,整段扫成若干 Paragraph）,
/// 所以这里不需要「是不是 markdown」的判断——.txt 走进来结果就是按空行分段,
/// 与改动前的定长切相比只是边界更整齐。
/// </summary>
public static class MarkdownBlockScanner
{
    /// <summary>Markdown 标题最深级数</summary>
    private const int MaxHeadingLevel = 6;

    /// <summary>标题路径的连接符。切块器要按层级比较路径,所以这个分隔符是共享契约</summary>
    public const string HeaderPathSeparator = " / ";

    /// <summary>围栏标记。两种都要认,不然用 ~~~ 的文档会被当成普通段落切开</summary>
    private static readonly string[] FenceMarkers = ["```", "~~~"];

    /// <summary>
    /// 扫描文本
    /// </summary>
    /// <param name="text">来源文本,换行会先归一成 \n</param>
    /// <returns>结构块序列;空白文本返回空序列</returns>
    public static IEnumerable<MemoryTextBlock> Scan(string text)
    {
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (normalized.Length == 0) yield break;

        string[] lines = normalized.Split('\n');
        string?[] headers = new string?[MaxHeadingLevel + 1];
        StringBuilder buffer = StringBuilderPool.Get();

        try
        {
            string headerPath = "";
            EMemoryBlockKind kind = EMemoryBlockKind.Paragraph;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                if (TryReadFenceMarker(line, out string fence))
                {
                    if (Flush(buffer, kind, headerPath) is { } beforeFence) yield return beforeFence;

                    // 围栏内的一切都按原样收进来:里面的 # 和 | 是代码,不是结构
                    buffer.AppendLine(line);
                    while (++i < lines.Length)
                    {
                        buffer.AppendLine(lines[i]);
                        if (IsFenceClose(lines[i], fence)) break;
                    }

                    if (Flush(buffer, EMemoryBlockKind.Code, headerPath) is { } code) yield return code;
                    kind = EMemoryBlockKind.Paragraph;
                    continue;
                }

                if (TryReadHeading(line, out int level, out string title))
                {
                    if (Flush(buffer, kind, headerPath) is { } beforeHeading) yield return beforeHeading;

                    headers[level] = title;
                    Array.Clear(headers, level + 1, headers.Length - level - 1); //更深的标题在新标题下失效
                    headerPath = BuildHeaderPath(headers);
                    kind = EMemoryBlockKind.Paragraph;
                    continue;
                }

                if (line.Trim().Length == 0)
                {
                    if (Flush(buffer, kind, headerPath) is { } beforeBlank) yield return beforeBlank;
                    kind = EMemoryBlockKind.Paragraph;
                    continue;
                }

                // 表格与正文相邻时之间未必有空行,类型一变就得断开
                bool isTableRow = IsTableRow(line);
                if (buffer.Length > 0 && isTableRow != (kind == EMemoryBlockKind.Table) &&
                    Flush(buffer, kind, headerPath) is { } beforeKindChange)
                {
                    yield return beforeKindChange;
                }

                kind = isTableRow ? EMemoryBlockKind.Table : EMemoryBlockKind.Paragraph;
                buffer.AppendLine(line);
            }

            if (Flush(buffer, kind, headerPath) is { } last) yield return last;
        }
        finally
        {
            StringBuilderPool.Release(buffer);
        }
    }

    /// <summary>
    /// 取表格的表头行(含分隔行),供按行切开时重复带上
    /// </summary>
    /// <param name="tableText">整张表的文本</param>
    /// <returns>表头部分;不像表格时返回空串</returns>
    public static string GetTableHeader(string tableText)
    {
        string[] lines = tableText.Split('\n');
        if (lines.Length < 2 || !IsTableRow(lines[0])) return "";

        return IsTableSeparatorRow(lines[1])
            ? lines[0] + "\n" + lines[1]
            : lines[0];
    }

    /// <summary>把攒着的行收成一个块并清空缓冲。没内容时返回 null</summary>
    private static MemoryTextBlock? Flush(StringBuilder buffer, EMemoryBlockKind kind, string headerPath)
    {
        if (buffer.Length == 0) return null;

        string content = buffer.ToString().Trim();
        buffer.Clear();
        return content.Length > 0 ? new MemoryTextBlock(kind, content, headerPath) : null;
    }

    private static string BuildHeaderPath(string?[] headers)
    {
        StringBuilder builder = StringBuilderPool.Get();
        foreach (string? header in headers)
        {
            if (string.IsNullOrEmpty(header)) continue;
            if (builder.Length > 0) builder.Append(HeaderPathSeparator);
            builder.Append(header);
        }

        string path = builder.ToString();
        StringBuilderPool.Release(builder);
        return path;
    }

    /// <summary>ATX 标题:行首 1-6 个 # 后跟空白</summary>
    private static bool TryReadHeading(string line, out int level, out string title)
    {
        level = 0;
        title = "";

        int index = 0;
        while (index < line.Length && line[index] == '#') index++;
        if (index == 0 || index > MaxHeadingLevel) return false;
        if (index >= line.Length || !char.IsWhiteSpace(line[index])) return false;

        level = index;
        title = line[index..].Trim();
        return title.Length > 0;
    }

    /// <summary>围栏起始行:``` 或 ~~~,允许缩进与语言标注</summary>
    private static bool TryReadFenceMarker(string line, out string fence)
    {
        fence = "";
        string trimmed = line.TrimStart();
        foreach (string marker in FenceMarkers)
        {
            if (!trimmed.StartsWith(marker, StringComparison.Ordinal)) continue;
            fence = marker;
            return true;
        }

        return false;
    }

    private static bool IsFenceClose(string line, string fence)
    {
        return line.TrimStart().StartsWith(fence, StringComparison.Ordinal);
    }

    /// <summary>表格行:去掉缩进后以 | 开头</summary>
    private static bool IsTableRow(string line)
    {
        string trimmed = line.TrimStart();
        return trimmed.StartsWith('|');
    }

    /// <summary>表格分隔行:只由 | - : 和空白组成</summary>
    private static bool IsTableSeparatorRow(string line)
    {
        string trimmed = line.Trim();
        if (!trimmed.StartsWith('|')) return false;

        foreach (char character in trimmed)
        {
            if (character is not ('|' or '-' or ':') && !char.IsWhiteSpace(character)) return false;
        }

        return true;
    }
}
