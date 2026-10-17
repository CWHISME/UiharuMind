/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>被省略的一段原文：字符区间与所在行（1 起算；整行拆法以外 LineCount 按跨过的换行算）</summary>
internal readonly record struct ElidedGap(int Start, int End, int FirstLine, int LineCount);

/// <summary>按省略记号拆段的定位结果；Kind 不是 Found 时 Error 是给模型的话术</summary>
internal sealed record ElidedMatch(ELocateResult Kind, int Start, int End, IReadOnlyList<ElidedGap> Gaps, bool LineMode,
    string? Error = null);

/// <summary>
/// oldString 里的省略记号（ADR 0088）：「开头 + <see cref="Token"/> + 结尾」替换从开头到结尾的整段。
/// 开头须唯一，之后每段取前一段之后最近的一处；记号独占一行时按整行比对（逐行 TrimEnd、全角归一），否则按原文精确找。
/// 只有一行且以记号收尾时表示「以此前那串开头的整行」——前缀锚在行首、行尾成为记号代表的省略段。
/// 只在整串按原文的各层匹配都不中时才用，所以文件里真有这串字符时照常命中。
/// </summary>
internal static class EditElision
{
    /// <summary>省略记号</summary>
    internal const string Token = @"[\s\S]*?";

    internal static bool Contains(string text) => text.Contains(Token, StringComparison.Ordinal);

    internal static int Count(string text)
    {
        int count = 0;
        for (int at = text.IndexOf(Token, StringComparison.Ordinal); at >= 0; at = text.IndexOf(Token, at + Token.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    /// <summary>
    /// 按省略记号拆段定位
    /// </summary>
    /// <param name="text">原文</param>
    /// <param name="lines">原文的行</param>
    /// <param name="oldString">含省略记号的 oldString</param>
    /// <param name="index">条目序号（话术用）</param>
    /// <param name="label">文件名（话术用）</param>
    /// <returns>定位结果</returns>
    internal static ElidedMatch Locate(string text, List<TextLine> lines, string oldString, int index, string label)
    {
        string normalized = oldString.Replace("\r\n", "\n");
        if (IsLinePrefix(normalized)) return LocateLinePrefix(text, lines, normalized, index, label);

        string[] oldLines = normalized.Split('\n');
        bool lineMode = oldLines.Count(line => line.Trim() == Token) == Count(normalized);
        return lineMode
            ? LocateLines(text, lines, oldLines, index, label)
            : LocateInline(text, lines, normalized, index, label);
    }

    // 只有一行、且记号是该行最后一个内容：表示「以此前那串开头的整行」
    private static bool IsLinePrefix(string normalized) =>
        normalized.IndexOf('\n') < 0
        && Count(normalized) == 1
        && normalized.TrimEnd().EndsWith(Token, StringComparison.Ordinal);

    /// <summary>
    /// 用定位到的省略区间拼出替换文本：newString 不含记号时整段换成它；含记号时按顺序把被省略的原文原样填回
    /// </summary>
    /// <param name="newString">模型给的 newString</param>
    /// <param name="text">原文</param>
    /// <param name="match">定位结果</param>
    /// <param name="convert">newString 的换行转写（被省略的原文不经过它，一字不改）</param>
    /// <returns>替换文本；记号数量对不上时为 null</returns>
    internal static string? Fill(string newString, string text, ElidedMatch match, Func<string, string> convert)
    {
        string normalized = newString.Replace("\r\n", "\n");
        int tokens = Count(normalized);
        if (tokens == 0) return convert(newString);
        if (tokens != match.Gaps.Count) return null;

        List<string> parts = [];
        List<bool> dropTerminator = []; //整行拆法里记号行后面没有换行时，填回的原文也去掉末尾换行
        int cursor = 0;
        for (int at = normalized.IndexOf(Token, StringComparison.Ordinal); at >= 0; at = normalized.IndexOf(Token, cursor, StringComparison.Ordinal))
        {
            int start = at;
            int end = at + Token.Length;
            bool drop = false;
            if (match.LineMode)
            {
                int lineStart = at == 0 ? 0 : normalized.LastIndexOf('\n', at - 1) + 1;
                int lineEnd = normalized.IndexOf('\n', end);
                if (normalized.AsSpan(lineStart, (lineEnd < 0 ? normalized.Length : lineEnd) - lineStart).Trim().SequenceEqual(Token))
                {
                    start = lineStart;
                    end = lineEnd < 0 ? normalized.Length : lineEnd + 1;
                    drop = lineEnd < 0;
                }
            }

            parts.Add(normalized[cursor..start]);
            dropTerminator.Add(drop);
            cursor = end;
        }

        parts.Add(normalized[cursor..]);

        System.Text.StringBuilder sb = new();
        for (int i = 0; i < match.Gaps.Count; i++)
        {
            sb.Append(convert(parts[i]));
            ElidedGap gap = match.Gaps[i];
            ReadOnlySpan<char> original = text.AsSpan(gap.Start, gap.End - gap.Start);
            if (dropTerminator[i] && original.EndsWith("\n")) original = original[..^(original.EndsWith("\r\n") ? 2 : 1)];
            sb.Append(original);
        }

        sb.Append(convert(parts[^1]));
        return sb.ToString();
    }

    /// <summary>成功回执里的一句：每处省略实际覆盖了哪几行</summary>
    internal static string Describe(int index, ElidedMatch match) =>
        $"edits[{index}] 的省略部分：" + string.Join("；", match.Gaps.Select(gap =>
            gap.End == gap.Start ? "空（前后两段紧挨着）"
            : !match.LineMode && gap.LineCount <= 1 ? $"第 {gap.FirstLine} 行内 {gap.End - gap.Start} 个字符"
            : $"第 {gap.FirstLine}–{gap.FirstLine + gap.LineCount - 1} 行（{gap.LineCount} 行）")) + "。";

    /// <summary>
    /// 没生效的省略条目的话术，对着 text 出（部分写入时是写入后的新文本，ADR 0087）
    /// </summary>
    /// <param name="index">条目序号</param>
    /// <param name="oldString">oldString</param>
    /// <param name="newString">newString</param>
    /// <param name="text">当前文本</param>
    /// <param name="lines">当前文本的行</param>
    /// <param name="label">文件名</param>
    /// <returns>话术</returns>
    internal static string DescribeSkipped(int index, string oldString, string newString, string text, List<TextLine> lines, string label)
    {
        ElidedMatch now = Locate(text, lines, oldString, index, label);
        if (now.Kind != ELocateResult.Found) return now.Error!;

        int tokens = Count(newString.Replace("\r\n", "\n"));
        if (tokens != 0 && tokens != now.Gaps.Count)
        {
            return $"edits[{index}] 的 oldString 有 {now.Gaps.Count} 处省略记号 {Token}，newString 里却有 {tokens} 处。"
                   + "这一条里 newString 的记号代表原样保留被省略的原文：要么一处不写（整段换成 newString），要么与 oldString 一样多。"
                   + $"要写入字面的 {Token}，把这一条改成不省略的普通编辑。";
        }

        return $"edits[{index}] 对着原文定位不上，未生效；其余编辑写入后它恰好能命中，原样重发即可。";
    }

    // 记号独占一行：各段按整行窗口比对
    private static ElidedMatch LocateLines(string text, List<TextLine> lines, string[] oldLines, int index, string label)
    {
        List<string[]> segments = [[]];
        foreach (string line in oldLines)
        {
            if (line.Trim() == Token) segments.Add([]);
            else segments[^1] = [.. segments[^1], line];
        }

        // oldString 以换行收尾时，末段的空行表示「连这一行的换行一起换掉」
        bool consumesTerminator = segments[^1].Length > 1 && segments[^1][^1].Length == 0;
        if (consumesTerminator) segments[^1] = segments[^1][..^1];

        if (Validate(segments.Select(segment => string.Join('\n', segment)).ToList(), index) is { } invalid)
            return Fail(ELocateResult.NotFound, invalid);

        List<int> heads = [];
        for (int i = 0; i + segments[0].Length <= lines.Count; i++)
        {
            if (WindowMatches(text, lines, i, segments[0])) heads.Add(i);
        }

        if (heads.Count == 0) return Fail(ELocateResult.NotFound, Missing(index, 0, string.Join('\n', segments[0]), text, lines, label));
        if (heads.Count > 1) return Fail(ELocateResult.NotUnique, NotUnique(index, heads.Select(h => h + 1).ToList(), label));

        List<ElidedGap> gaps = [];
        int cursor = heads[0] + segments[0].Length;
        for (int k = 1; k < segments.Count; k++)
        {
            int found = -1;
            for (int i = cursor; i + segments[k].Length <= lines.Count && found < 0; i++)
            {
                if (WindowMatches(text, lines, i, segments[k])) found = i;
            }

            if (found < 0) return Fail(ELocateResult.NotFound, Missing(index, k, string.Join('\n', segments[k]), text, lines, label));

            int gapStart = cursor < lines.Count ? lines[cursor].Start : text.Length;
            gaps.Add(new ElidedGap(gapStart, lines[found].Start, cursor + 1, found - cursor));
            cursor = found + segments[k].Length;
        }

        TextLine last = lines[cursor - 1];
        return new ElidedMatch(ELocateResult.Found, lines[heads[0]].Start, consumesTerminator ? last.End : last.ContentEnd, gaps, true);
    }

    // 记号夹在行内：各段按原文精确找
    private static ElidedMatch LocateInline(string text, List<TextLine> lines, string oldString, int index, string label)
    {
        List<string> segments = oldString.Split(Token).ToList();
        if (Validate(segments, index) is { } invalid) return Fail(ELocateResult.NotFound, invalid);

        // CRLF 文件里多行的段要按文件的换行找
        string newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        segments = segments.Select(segment => segment.Replace("\n", newLine)).ToList();

        List<int> heads = [];
        for (int at = text.IndexOf(segments[0], StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(segments[0], at + segments[0].Length, StringComparison.Ordinal))
            heads.Add(at);

        if (heads.Count == 0) return Fail(ELocateResult.NotFound, Missing(index, 0, segments[0], text, lines, label));
        if (heads.Count > 1)
            return Fail(ELocateResult.NotUnique, NotUnique(index, heads.Select(h => EditLocator.LineOf(lines, h)).ToList(), label));

        List<ElidedGap> gaps = [];
        int cursor = heads[0] + segments[0].Length;
        for (int k = 1; k < segments.Count; k++)
        {
            int found = text.IndexOf(segments[k], cursor, StringComparison.Ordinal);
            if (found < 0) return Fail(ELocateResult.NotFound, Missing(index, k, segments[k], text, lines, label));

            int firstLine = EditLocator.LineOf(lines, cursor);
            gaps.Add(new ElidedGap(cursor, found, firstLine, found == cursor ? 0 : EditLocator.LineOf(lines, found) - firstLine + 1));
            cursor = found + segments[k].Length;
        }

        return new ElidedMatch(ELocateResult.Found, heads[0], cursor, gaps, false);
    }

    // 单行前缀：逐行比行首，命中整行；行首之后的行尾成为省略段（newString 里的记号原样保留它）
    private static ElidedMatch LocateLinePrefix(string text, List<TextLine> lines, string oldString, int index, string label)
    {
        string prefix = oldString[..oldString.LastIndexOf(Token, StringComparison.Ordinal)].TrimEnd();
        if (prefix.Length == 0)
            return Fail(ELocateResult.NotFound,
                $"edits[{index}].oldString 里 {Token} 之前没有原文：单行以它收尾表示「以此前那串开头的整行」。");

        List<int> hits = [];
        for (int i = 0; i < lines.Count; i++)
        {
            ReadOnlySpan<char> fileLine = text.AsSpan(lines[i].Start, lines[i].ContentEnd - lines[i].Start).TrimEnd();
            if (StartsWith(fileLine, prefix)) hits.Add(i);
        }

        if (hits.Count == 0) return Fail(ELocateResult.NotFound, MissingLinePrefix(index, prefix, text, lines, label));
        if (hits.Count > 1) return Fail(ELocateResult.NotUnique, NotUniqueLinePrefix(index, prefix, hits, label));

        TextLine hit = lines[hits[0]];
        int gapStart = hit.Start + prefix.Length;
        List<ElidedGap> gaps = [new ElidedGap(gapStart, hit.ContentEnd, hits[0] + 1, hit.ContentEnd == gapStart ? 0 : 1)];
        return new ElidedMatch(ELocateResult.Found, hit.Start, hit.ContentEnd, gaps, false);
    }

    // 行首比对：原样相等或白名单全角归一后相等（复用整行口径）
    private static bool StartsWith(ReadOnlySpan<char> fileLine, ReadOnlySpan<char> prefix) =>
        fileLine.Length >= prefix.Length && EditLocator.LineEquals(fileLine[..prefix.Length], prefix);

    private static string MissingLinePrefix(int index, string prefix, string text, List<TextLine> lines, string label)
    {
        string[] fileLines = lines.Select(line => TextLines.Text(text, line)).ToArray();
        return $"edits[{index}].oldString 以 {Token} 收尾，表示以「{prefix}」开头的整行，但 '{label}' 里没有这样的行。"
               + "下面是对这串开头的匹配提示：\n"
               + EditMissHint.Build(index, prefix, label, fileLines);
    }

    private static string NotUniqueLinePrefix(int index, string prefix, List<int> hitLines, string label) =>
        $"edits[{index}].oldString 以 {Token} 收尾，表示以「{prefix}」开头的整行，"
        + $"但 '{label}' 中这样的行有 {hitLines.Count} 处（起始于第 {string.Join("、", hitLines.Select(h => h + 1))} 行）。"
        + "把这串开头写长一些，让它只匹配一行。";

    // 每段都得有内容；开头之后的段若只剩括号、标点，就近匹配几乎一定截短范围
    private static string? Validate(List<string> segments, int index)
    {
        if (segments.Any(segment => segment.Trim().Length == 0))
            return $"edits[{index}].oldString 里 {Token} 的前后都要有原文：它代表两段原文之间省略的部分。";

        int weak = segments.FindIndex(1, segment => !segment.Any(char.IsLetterOrDigit));
        return weak < 0
            ? null
            : $"edits[{index}].oldString 里 {Token} 之后的「{segments[weak].Trim()}」只有括号或标点：工具取开头之后最近的一处，"
              + "这样的结尾几乎一定会匹配得太早。把结尾写长一些，带上一行有字母或数字的原文。";
    }

    private static bool WindowMatches(string text, List<TextLine> lines, int first, string[] pieces)
    {
        for (int j = 0; j < pieces.Length; j++)
        {
            TextLine line = lines[first + j];
            if (!EditLocator.LineEquals(text.AsSpan(line.Start, line.ContentEnd - line.Start).TrimEnd(), pieces[j].AsSpan().TrimEnd()))
                return false;
        }

        return true;
    }

    private static string Missing(int index, int segment, string segmentText, string text, List<TextLine> lines, string label)
    {
        string where = segment == 0 ? "开头一段" : $"第 {segment + 1} 段（在前一段之后找）";
        string[] fileLines = lines.Select(line => TextLines.Text(text, line)).ToArray();
        return $"edits[{index}] 用了省略记号 {Token}，{where}没有找到。下面是这一段的匹配提示：\n"
               + EditMissHint.Build(index, segmentText.Replace("\r\n", "\n"), label, fileLines);
    }

    private static string NotUnique(int index, IReadOnlyList<int> startLines, string label) =>
        $"edits[{index}] 用了省略记号 {Token}，开头一段在 '{label}' 中出现了 {startLines.Count} 次"
        + $"（起始于第 {string.Join("、", startLines)} 行）。给开头补上几行，让它只匹配一处。";

    private static ElidedMatch Fail(ELocateResult kind, string error) => new(kind, -1, -1, [], false, error);
}
