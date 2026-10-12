namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>定位结果的类别</summary>
internal enum ELocateResult
{
    Found,
    NotFound,
    NotUnique,
}

/// <summary>oldString 的定位结果；NotUnique 时 NormalizedAmbiguity 表示有命中只靠全角归一才对上</summary>
internal readonly record struct Location(ELocateResult Kind, int Start, int End, int Count,
    bool NormalizedAmbiguity = false);

/// <summary>
/// 在原文里找 oldString：精确 → 整行窗口 TrimEnd → 受控全角归一，规则与边界见 <see cref="FileEditPlanner"/>
/// </summary>
internal static class EditLocator
{
    internal static Location Locate(string text, List<TextLine> lines, string oldString)
    {
        // 精确：数全部出现次数（不重叠计数），多于一处就交回给模型加上下文
        int count = 0;
        int first = -1;
        int from = 0;
        while (from <= text.Length - oldString.Length)
        {
            int at = text.IndexOf(oldString, from, StringComparison.Ordinal);
            if (at < 0) break;
            if (first < 0) first = at;
            count++;
            from = at + oldString.Length;
        }

        if (count == 1) return new Location(ELocateResult.Found, first, first + oldString.Length, 1);
        if (count > 1) return new Location(ELocateResult.NotUnique, -1, -1, count);

        return LocateByLineWindow(text, lines, oldString);
    }

    /// <summary>
    /// 保守 fuzzy：把 oldString 当成一段<b>完整的行</b>，逐行 TrimEnd 后与文件行窗口比对。
    /// 只服务多行/整行的 oldString——行内的片段精确匹配就能中，中不了也不该靠猜。
    /// </summary>
    private static Location LocateByLineWindow(string text, List<TextLine> lines, string oldString)
    {
        string[] pieces = oldString.Replace("\r\n", "\n").Split('\n');
        // oldString 以换行收尾时，末段是空串：它表示"连这一行的换行一起换掉"
        bool consumesTerminator = pieces.Length > 1 && pieces[^1].Length == 0;
        int keyCount = consumesTerminator ? pieces.Length - 1 : pieces.Length;
        if (keyCount == 0) return new Location(ELocateResult.NotFound, -1, -1, 0);

        int count = 0;
        int firstLine = -1;
        bool sawNormalizedOnly = false; //至少一个命中窗口仅靠全角归一才匹配上
        for (int i = 0; i + keyCount <= lines.Count; i++)
        {
            bool hit = true;
            bool exact = true;
            for (int j = 0; j < keyCount; j++)
            {
                TextLine line = lines[i + j];
                ReadOnlySpan<char> fileLine = text.AsSpan(line.Start, line.ContentEnd - line.Start).TrimEnd();
                ReadOnlySpan<char> anchor = pieces[j].AsSpan().TrimEnd();
                if (fileLine.SequenceEqual(anchor)) continue;

                exact = false;
                // 受控全角归一兜底：白名单标点映射后再比一次（只影响判定，不落盘）
                if (FullWidthNormalizedEquals(fileLine, anchor)) continue;

                hit = false;
                break;
            }

            if (!hit) continue;
            if (firstLine < 0) firstLine = i;
            count++;
            if (!exact) sawNormalizedOnly = true;
        }

        if (count == 0) return new Location(ELocateResult.NotFound, -1, -1, 0);
        if (count > 1) return new Location(ELocateResult.NotUnique, -1, -1, count, sawNormalizedOnly);

        TextLine last = lines[firstLine + keyCount - 1];
        return new Location(ELocateResult.Found, lines[firstLine].Start,
            consumesTerminator ? last.End : last.ContentEnd, 1);
    }

    /// <summary>
    /// 受控全角归一：把白名单全角标点映射成半角后再比较（只影响判定，不落盘）。
    /// 快速路径：两边都没有白名单字符就直接返回 false（不等），避免无谓扫描。
    /// 映射是<b>逐个字符</b>的——不在白名单里的字符原样保留，因此长度不变。
    /// </summary>
    private static bool FullWidthNormalizedEquals(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        if (a.Length != b.Length) return false;
        if (!ContainsFullWidthPunctuation(a) && !ContainsFullWidthPunctuation(b)) return false;

        for (int i = 0; i < a.Length; i++)
        {
            if (MapFullWidth(a[i]) != MapFullWidth(b[i])) return false;
        }

        return true;
    }

    private static bool ContainsFullWidthPunctuation(ReadOnlySpan<char> s)
    {
        foreach (char c in s)
        {
            if (c is '，' or '。' or '：' or '；' or '（' or '）' or '！' or '？' or '\u3000') return true;
        }

        return false;
    }

    /// <summary>白名单映射：只把无语义冲突的全角标点归成半角，其余原样保留（含长度不变）</summary>
    private static char MapFullWidth(char c) => c switch
    {
        '，' => ',',
        '。' => '.',
        '：' => ':',
        '；' => ';',
        '（' => '(',
        '）' => ')',
        '！' => '!',
        '？' => '?',
        '　' => ' ',
        _ => c,
    };
}
