namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>一行在原文中的位置</summary>
/// <param name="Start">行首在原文里的偏移</param>
/// <param name="ContentEnd">正文结束处（不含行终止符）</param>
/// <param name="End">含行终止符的结束处</param>
internal readonly record struct TextLine(int Start, int ContentEnd, int End);

/// <summary>
/// 文件工具唯一的切行口径：<c>\n</c>、<c>\r\n</c> 与单独的 <c>\r</c> 都是行终止符，同 <see cref="StreamReader.ReadLine"/>。
/// Read 的行号、大纲的行号、Edit 定位与失败提示里的行号都从这里来，才彼此对得上（含流式读的大文件）。
/// 末尾的终止符不另起空行：<c>"a\n"</c> 是一行。
/// </summary>
internal static class TextLines
{
    /// <summary>
    /// 切行，保留每行在原文里的位置
    /// </summary>
    /// <param name="text">正文</param>
    /// <returns>各行位置，按顺序</returns>
    public static List<TextLine> Split(string text)
    {
        List<TextLine> lines = new();
        int start = 0;
        while (start < text.Length)
        {
            int end = text.AsSpan(start).IndexOfAny('\r', '\n');
            if (end < 0)
            {
                lines.Add(new TextLine(start, text.Length, text.Length));
                break;
            }

            end += start;
            int next = end + (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1);
            lines.Add(new TextLine(start, end, next));
            start = next;
        }

        return lines;
    }

    /// <summary>
    /// 逐行取正文（不含终止符）
    /// </summary>
    /// <param name="text">正文</param>
    /// <returns>各行文本</returns>
    public static IEnumerable<string> Enumerate(string text) => Split(text).Select(line => Text(text, line));

    /// <summary>
    /// 取一行的正文
    /// </summary>
    /// <param name="text">原文</param>
    /// <param name="line">行位置</param>
    /// <returns>该行正文（不含终止符）</returns>
    public static string Text(string text, TextLine line) => text[line.Start..line.ContentEnd];

    /// <summary>
    /// 某个偏移落在第几行（0 起算）；越界时夹到首末行
    /// </summary>
    /// <param name="lines">各行位置</param>
    /// <param name="offset">原文偏移</param>
    /// <returns>行下标</returns>
    public static int IndexOf(List<TextLine> lines, int offset)
    {
        int low = 0;
        int high = lines.Count - 1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (offset < lines[mid].Start) high = mid - 1;
            else if (offset >= lines[mid].End) low = mid + 1;
            else return mid;
        }

        return Math.Clamp(low, 0, Math.Max(0, lines.Count - 1));
    }
}
