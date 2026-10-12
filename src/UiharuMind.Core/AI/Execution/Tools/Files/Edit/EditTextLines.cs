namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>编辑里的一行在原文中的位置</summary>
/// <param name="Start">行首在原文里的偏移</param>
/// <param name="ContentEnd">正文结束处（不含行终止符）</param>
/// <param name="End">含行终止符的结束处</param>
internal readonly record struct TextLine(int Start, int ContentEnd, int End);

/// <summary>一处已定位的编辑：原文区间 [Start, End) 换成 NewText；Index 是它在 edits 里的序号</summary>
internal readonly record struct EditMatch(int Index, int Start, int End, string NewText);

/// <summary>编辑用的切行与按偏移找行，定位与 diff 共用同一份行口径</summary>
internal static class EditTextLines
{
    /// <summary>
    /// 切行，保留每行在原文里的三个位置。<c>\r\n</c> 与 <c>\n</c> 都算终止符且不计入
    /// <see cref="TextLine.ContentEnd"/>，行尾比较因此天然对行尾风格不敏感。
    /// </summary>
    internal static List<TextLine> Split(string text)
    {
        List<TextLine> lines = new();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            int contentEnd = i > start && text[i - 1] == '\r' ? i - 1 : i;
            lines.Add(new TextLine(start, contentEnd, i + 1));
            start = i + 1;
        }

        if (start < text.Length) lines.Add(new TextLine(start, text.Length, text.Length));
        return lines;
    }

    internal static string Text(string text, TextLine line) => text[line.Start..line.ContentEnd];

    internal static int IndexOf(List<TextLine> lines, int offset)
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
