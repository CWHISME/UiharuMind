namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>定位结果的类别</summary>
internal enum ELocateResult
{
    Found,
    NotFound,
    NotUnique,
}

/// <summary>
/// oldString 的定位结果；NotUnique 时 NormalizedAmbiguity 表示有命中只靠全角归一才对上，
/// Lines 是各处命中的起始行号（1 起算），让模型知道去哪几行补上下文
/// </summary>
internal readonly record struct Location(ELocateResult Kind, int Start, int End, int Count,
    bool NormalizedAmbiguity = false, IReadOnlyList<int>? Lines = null);

/// <summary>
/// 在原文里找 oldString：精确 → 整行窗口 TrimEnd → 受控全角归一（缩进偏移另由调用方兜底），规则与边界见 <see cref="FileEditPlanner"/>
/// </summary>
internal static class EditLocator
{
    internal static Location Locate(string text, List<TextLine> lines, string oldString)
    {
        // 精确：数全部出现次数（不重叠计数），多于一处就交回给模型加上下文
        List<int> starts = [];
        int from = 0;
        while (from <= text.Length - oldString.Length)
        {
            int at = text.IndexOf(oldString, from, StringComparison.Ordinal);
            if (at < 0) break;
            starts.Add(at);
            from = at + oldString.Length;
        }

        if (starts.Count == 1) return new Location(ELocateResult.Found, starts[0], starts[0] + oldString.Length, 1);
        if (starts.Count > 1)
        {
            return new Location(ELocateResult.NotUnique, -1, -1, starts.Count,
                Lines: starts.Select(start => LineOf(lines, start)).ToList());
        }

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

        List<int> hitLines = [];
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
            hitLines.Add(i);
            if (!exact) sawNormalizedOnly = true;
        }

        if (hitLines.Count == 0) return new Location(ELocateResult.NotFound, -1, -1, 0);
        if (hitLines.Count > 1)
        {
            return new Location(ELocateResult.NotUnique, -1, -1, hitLines.Count, sawNormalizedOnly,
                hitLines.Select(line => line + 1).ToList());
        }

        int firstLine = hitLines[0];

        TextLine last = lines[firstLine + keyCount - 1];
        return new Location(ELocateResult.Found, lines[firstLine].Start,
            consumesTerminator ? last.End : last.ContentEnd, 1);
    }

    /// <summary>
    /// 缩进偏移兜底：oldString 每个非空行与文件行窗口只差<b>同一段</b>行首空白（整段多一层或少一层）。
    /// 常见于模型删 Read 行号时多删或少删了与缩进同形的制表符。只在前面几层都没命中时用
    /// </summary>
    /// <param name="text">原文</param>
    /// <param name="lines">原文的行</param>
    /// <param name="oldString">模型给的 oldString</param>
    /// <param name="added">文件比 oldString 每行多出的行首空白；没有为空串</param>
    /// <param name="removed">oldString 比文件每行多出的行首空白；没有为空串</param>
    /// <returns>定位结果</returns>
    internal static Location LocateByIndentShift(string text, List<TextLine> lines, string oldString,
        out string added, out string removed)
    {
        added = string.Empty;
        removed = string.Empty;
        string[] pieces = oldString.Replace("\r\n", "\n").Split('\n');
        bool consumesTerminator = pieces.Length > 1 && pieces[^1].Length == 0;
        int keyCount = consumesTerminator ? pieces.Length - 1 : pieces.Length;
        int anchorIndex = Array.FindIndex(pieces, 0, keyCount, piece => piece.AsSpan().TrimEnd().Length > 0);
        if (anchorIndex < 0) return new Location(ELocateResult.NotFound, -1, -1, 0);

        string anchorPiece = pieces[anchorIndex].TrimEnd();
        List<(int Line, int Added, int Removed)> hits = []; //偏移量记长度，命中后才从文件/oldString 里切出那段空白
        for (int i = 0; i + keyCount <= lines.Count; i++)
        {
            ReadOnlySpan<char> anchorFile = FileLine(text, lines[i + anchorIndex]);
            int add = 0;
            int remove = 0;
            if (anchorFile.Length > anchorPiece.Length && anchorFile.EndsWith(anchorPiece, StringComparison.Ordinal))
                add = anchorFile.Length - anchorPiece.Length;
            else if (anchorPiece.Length > anchorFile.Length && anchorPiece.AsSpan().EndsWith(anchorFile, StringComparison.Ordinal))
                remove = anchorPiece.Length - anchorFile.Length;
            ReadOnlySpan<char> shift = add > 0 ? anchorFile[..add] : anchorPiece.AsSpan(0, remove);
            if (shift.IsEmpty || !shift.IsWhiteSpace()) continue;

            bool hit = true;
            for (int j = 0; j < keyCount && hit; j++)
            {
                ReadOnlySpan<char> fileLine = FileLine(text, lines[i + j]);
                ReadOnlySpan<char> piece = pieces[j].AsSpan().TrimEnd();
                // 只按锚行定下的那个方向比：两边都比的话，没偏移的行也会算命中
                if (piece.IsEmpty) hit = fileLine.IsEmpty;
                else if (add > 0) hit = fileLine.Length == piece.Length + add && fileLine.StartsWith(shift) && fileLine[add..].SequenceEqual(piece);
                else hit = piece.Length == fileLine.Length + remove && piece.StartsWith(shift) && piece[remove..].SequenceEqual(fileLine);
            }

            if (hit) hits.Add((i, add, remove));
        }

        if (hits.Count == 0) return new Location(ELocateResult.NotFound, -1, -1, 0);
        if (hits.Count > 1)
        {
            return new Location(ELocateResult.NotUnique, -1, -1, hits.Count,
                Lines: hits.Select(hit => hit.Line + 1).ToList());
        }

        (int firstLine, int addedLength, int removedLength) = hits[0];
        added = FileLine(text, lines[firstLine + anchorIndex])[..addedLength].ToString();
        removed = anchorPiece[..removedLength];
        TextLine last = lines[firstLine + keyCount - 1];
        return new Location(ELocateResult.Found, lines[firstLine].Start,
            consumesTerminator ? last.End : last.ContentEnd, 1);
    }

    private static ReadOnlySpan<char> FileLine(string text, TextLine line) =>
        text.AsSpan(line.Start, line.ContentEnd - line.Start).TrimEnd();

    // 字符偏移所在的行号（1 起算）
    internal static int LineOf(List<TextLine> lines, int offset)
    {
        int low = 0;
        int high = lines.Count - 1;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (lines[mid].Start <= offset) low = mid;
            else high = mid - 1;
        }

        return low + 1;
    }

    /// <summary>整行比对口径（两边已 TrimEnd）：原样相等，或白名单全角归一后相等</summary>
    internal static bool LineEquals(ReadOnlySpan<char> fileLine, ReadOnlySpan<char> anchor) =>
        fileLine.SequenceEqual(anchor) || FullWidthNormalizedEquals(fileLine, anchor);

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
