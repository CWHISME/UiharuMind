/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// Edit 的 oldString 没找到时的话术：在标准提示后附上「最近的整块」与逐行对照，让模型一步改对，而不是反复读抄。
///
/// 两种对齐取更能解释差异的那个：
/// <list type="bullet">
/// <item>等长窗口：oldString 逐行贴着文件滑，按字符编辑距离评分。错字、整段改写、只差缩进都靠它——
/// 单行级评分会把「内容全等、只差缩进」的行输给远处一句相近的注释，块级才回得到同一个地方。</item>
/// <item>带缺行/多行的行对齐：中间多抄一行或漏抄一行时，等长窗口后半段整体错位，会被远处一个长得像的方法抢走。
/// 这一轮按行计代价（相同 0，改写、多出、缺失各 1），把多出与缺失的行单独标出来。</item>
/// </list>
/// 逐行预览里连续 [ok] 行折叠成摘要——那些行模型自己刚发过，原样返回纯属双倍浪费。
/// </summary>
internal static class EditMissHint
{
    private const int MaxBlockLines = 40; //最多评多少行的块：再大就放弃，评分随块行数变慢
    private const int MaxShownRows = 20; //最多列出多少条对照，超出折叠
    private const int MaxLineChars = 200; //超长行算编辑距离不划算，不做候选

    /// <summary>
    /// 组装 NotFound 话术
    /// </summary>
    /// <param name="index">第几条编辑</param>
    /// <param name="oldString">模型给的 oldString</param>
    /// <param name="label">出现在话术里的文件名</param>
    /// <param name="fileLines">文件逐行正文（不含行终止符）</param>
    /// <returns>给模型的话术</returns>
    public static string Build(int index, string oldString, string label, IReadOnlyList<string> fileLines)
    {
        string message = $"edits[{index}].oldString was not found in '{label}'. It must match the file exactly, "
                         + "whitespace and line breaks included. Follow the Closest match hint below to fix the oldString; "
                         + "only when there is no candidate or the location is unclear, Read a small range around the hint "
                         + "— don't re-read the whole file.";

        string[] anchors = oldString.Split('\n').Select(a => a.TrimEnd()).ToArray();
        // 镜像 LocateByLineWindow：尾随换行产生的空末段只表示「连换行一起换」，不参与评分
        if (anchors.Length > 1 && anchors[^1].Length == 0) anchors = anchors[..^1];
        if (anchors.Length == 0 || anchors.Length > MaxBlockLines
            || anchors.Any(a => a.Length > MaxLineChars) || anchors.All(a => a.Length == 0))
        {
            return message;
        }

        string[] file = fileLines.Select(l => l.TrimEnd()).ToArray();
        Alignment? window = AlignByWindow(anchors, file);
        Alignment? gapped = anchors.Length >= 3 ? AlignWithGaps(anchors, file) : null;
        // 行对齐只在它真的用到了缺行/多行、且比等长窗口少解释不了的行时才采用
        Alignment? best = gapped != null && (window == null || gapped.Unmatched < window.Unmatched) ? gapped : window;

        return best == null ? message : message + Render(best, anchors, file);
    }

    #region 对齐

    /// <summary>等长窗口：逐窗按字符编辑距离评分，同分保留更早的窗口</summary>
    private static Alignment? AlignByWindow(string[] anchors, string[] file)
    {
        int bestStart = -1;
        int bestCost = int.MaxValue;
        for (int start = 0; start + anchors.Length <= file.Length; start++)
        {
            int cost = 0;
            for (int i = 0; i < anchors.Length && cost < bestCost; i++)
            {
                string candidate = file[start + i];
                if (candidate.Length > MaxLineChars)
                {
                    cost = int.MaxValue;
                    break;
                }

                if (Classify(anchors[i], candidate) == EStep.Diff) cost += DamerauLevenshtein(anchors[i], candidate);
            }

            if (cost >= bestCost) continue;
            bestCost = cost;
            bestStart = start;
        }

        // 整块距离和超过锚字符数一半才算「不够像」，避免短行全相似误报
        if (bestStart < 0 || bestCost > anchors.Sum(a => a.Length) / 2) return null;

        var steps = new List<Step>(anchors.Length);
        for (int i = 0; i < anchors.Length; i++)
        {
            steps.Add(new Step(Classify(anchors[i], file[bestStart + i]), i, bestStart + i));
        }

        return new Alignment(steps);
    }

    /// <summary>
    /// 带缺行/多行的行对齐（半全局：oldString 必须整段对上，文件里起止自由）。
    /// 先滚动数组找最便宜的终点，再只在终点附近建全表回溯——总代价 ≤ 块长，对齐跨度就不会超过两倍块长
    /// </summary>
    private static Alignment? AlignWithGaps(string[] anchors, string[] file)
    {
        int n = anchors.Length;
        int m = file.Length;
        if (m == 0) return null;

        int[] anchorHash = anchors.Select(a => string.GetHashCode(a.AsSpan().Trim())).ToArray();
        int[] fileHash = file.Select(f => string.GetHashCode(f.AsSpan().Trim())).ToArray();

        bool Same(int i, int j) => anchorHash[i] == fileHash[j] && anchors[i].AsSpan().Trim().SequenceEqual(file[j].AsSpan().Trim());

        // 第一遍：滚动数组求每个终点的最小代价
        int[] prev = new int[m + 1];
        int[] curr = new int[m + 1];
        for (int i = 1; i <= n; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= m; j++)
            {
                int diagonal = prev[j - 1] + (Same(i - 1, j - 1) ? 0 : 1);
                curr[j] = Math.Min(diagonal, Math.Min(prev[j] + 1, curr[j - 1] + 1));
            }

            (prev, curr) = (curr, prev);
        }

        int end = 0;
        for (int j = 1; j <= m; j++)
        {
            if (prev[j] < prev[end]) end = j;
        }

        int cost = prev[end];
        if (cost == 0 || cost * 2 > n) return null; // 一半以上的行都对不上，不算候选

        // 第二遍：终点附近建全表回溯
        int lo = Math.Max(0, end - n - cost);
        int width = end - lo;
        var table = new int[n + 1, width + 1];
        for (int i = 1; i <= n; i++)
        {
            table[i, 0] = i;
            for (int j = 1; j <= width; j++)
            {
                int diagonal = table[i - 1, j - 1] + (Same(i - 1, lo + j - 1) ? 0 : 1);
                table[i, j] = Math.Min(diagonal, Math.Min(table[i - 1, j] + 1, table[i, j - 1] + 1));
            }
        }

        // 回溯时同代价优先走对角（改写比「多一行 + 缺一行」好读）
        var steps = new List<Step>();
        int ai = n;
        int fj = width;
        while (ai > 0)
        {
            bool same = fj > 0 && Same(ai - 1, lo + fj - 1);
            if (fj > 0 && table[ai, fj] == table[ai - 1, fj - 1] + (same ? 0 : 1))
            {
                EStep kind = same ? Classify(anchors[ai - 1], file[lo + fj - 1]) : EStep.Diff;
                steps.Add(new Step(kind, ai - 1, lo + fj - 1));
                ai--;
                fj--;
            }
            else if (table[ai, fj] == table[ai - 1, fj] + 1)
            {
                steps.Add(new Step(EStep.Extra, ai - 1, -1));
                ai--;
            }
            else
            {
                steps.Add(new Step(EStep.Missing, -1, lo + fj - 1));
                fj--;
            }
        }

        steps.Reverse();
        var alignment = new Alignment(steps);
        // 没用上缺行/多行就是等长窗口的形状，交给按字符评分的那一轮；对上的全是空行、括号这类也不算
        bool hasGap = steps.Any(s => s.Kind is EStep.Extra or EStep.Missing);
        bool anchoredByContent = steps.Any(s => s.Kind != EStep.Diff && s.Anchor >= 0 && s.File >= 0
                                                && anchors[s.Anchor].Trim().Length > 3);
        return hasGap && anchoredByContent ? alignment : null;
    }

    private static EStep Classify(string anchor, string candidate)
    {
        if (anchor == candidate) return EStep.Exact;
        return anchor.AsSpan().Trim().SequenceEqual(candidate.AsSpan().Trim()) ? EStep.Whitespace : EStep.Diff;
    }

    #endregion

    #region 话术

    private static string Render(Alignment alignment, string[] anchors, string[] file)
    {
        List<Step> steps = alignment.Steps;
        int firstFileLine = steps.First(s => s.File >= 0).File;

        var sb = new StringBuilder();
        sb.Append($" Closest match: line {firstFileLine + 1} ({alignment.ContentMatched}/{anchors.Length} lines content-matched)");

        List<string> rows = BuildRows(steps, anchors, file);
        int shown = Math.Min(rows.Count, MaxShownRows);
        for (int k = 0; k < shown; k++)
        {
            sb.Append($"\n   {rows[k]}");
        }

        if (rows.Count > shown) sb.Append($"\n   …(+{rows.Count - shown} more lines)");

        string? overrun = DescribeOverrun(steps, anchors, file);
        if (overrun != null) return sb.Append('\n').Append(overrun).ToString();

        if (alignment.HasGap) return sb.Append('\n').Append(DescribeGaps(alignment)).ToString();

        Step firstFix = steps.FirstOrDefault(s => s.Kind != EStep.Exact);
        if (firstFix.Kind != EStep.Exact)
        {
            string expected = anchors[firstFix.Anchor];
            string found = file[firstFix.File];
            sb.Append($"\nline {firstFix.File + 1}: expected '{Clamp(expected)}' but found '{Clamp(found)}'");
            sb.Append(firstFix.Kind == EStep.Diff ? " — content differs." : DescribeWhitespaceDiff(expected, found));
        }

        return sb.ToString();
    }

    /// <summary>连续 [ok]（≥2 行）折叠成摘要；差异行把模型自己那行也摆出来——只给文件侧，模型会对着刚读到的正文说服自己「明明一致」</summary>
    private static List<string> BuildRows(List<Step> steps, string[] anchors, string[] file)
    {
        var rows = new List<string>();
        int idx = 0;
        while (idx < steps.Count)
        {
            Step step = steps[idx];
            if (step.Kind == EStep.Exact)
            {
                int runStart = idx;
                while (idx < steps.Count && steps[idx].Kind == EStep.Exact) idx++;
                int runLen = idx - runStart;
                if (runLen >= 2)
                {
                    rows.Add($"{steps[runStart].File + 1}-{steps[idx - 1].File + 1} [ok] ({runLen} lines)");
                    continue;
                }

                idx = runStart;
            }

            rows.Add(step.Kind switch
            {
                EStep.Exact => $"{step.File + 1} [ok] {Clamp(file[step.File])}",
                EStep.Whitespace => $"{step.File + 1} [ws] {Clamp(file[step.File])}",
                EStep.Diff => $"{step.File + 1} [diff] {Clamp(file[step.File])}\n"
                              + $"      oldString line {step.Anchor + 1}: {Clamp(anchors[step.Anchor])}",
                EStep.Missing => $"{step.File + 1} [missing] {Clamp(file[step.File])}",
                _ => $"[extra] oldString line {step.Anchor + 1}: {Clamp(anchors[step.Anchor])}",
            });
            idx++;
        }

        return rows;
    }

    /// <summary>
    /// 差异全在 oldString 头部或尾部、其余逐字相同：多半是多带了文件里没有的行（以为后面还有一个方法、前面还有一行注释），
    /// 直说「超出了实际文本」，比逐行 expected/found 更难被模型否掉。只是错字（secon / second）不算
    /// </summary>
    private static string? DescribeOverrun(List<Step> steps, string[] anchors, string[] file)
    {
        if (steps.Any(s => s.Kind is EStep.Whitespace or EStep.Missing)) return null;
        int exact = steps.Count(s => s.Kind == EStep.Exact);
        int off = steps.Count - exact;
        if (exact == 0 || off == 0) return null;

        int firstOff = steps.FindIndex(s => s.Kind != EStep.Exact);
        int lastOff = steps.FindLastIndex(s => s.Kind != EStep.Exact);
        if (lastOff - firstOff + 1 != off) return null; // 差异不连续
        foreach (Step step in steps.Where(s => s.Kind == EStep.Diff))
        {
            string anchor = anchors[step.Anchor].Trim();
            string found = file[step.File].Trim();
            if (DamerauLevenshtein(anchor, found) * 2 <= Math.Max(anchor.Length, found.Length)) return null;
        }

        int n = anchors.Length;
        string noun = off == 1 ? "line is" : "lines are";
        string pronoun = off == 1 ? "it" : "them";
        if (lastOff == steps.Count - 1)
        {
            int matchedEnd = steps[firstOff - 1].File + 1;
            string next = matchedEnd < file.Length ? $"continues with '{Clamp(file[matchedEnd])}'" : "ends";
            return $"The first {exact} of {n} oldString lines match lines {steps[0].File + 1}-{matchedEnd} exactly, "
                   + $"but its last {off} {noun} not in the file: after line {matchedEnd} the file {next}. "
                   + $"Your oldString runs past the real text — drop or correct {pronoun}.";
        }

        if (firstOff == 0)
        {
            int matchedStart = steps[lastOff + 1].File + 1;
            string before = matchedStart > 1 ? $"has '{Clamp(file[matchedStart - 2])}'" : "starts";
            return $"The last {exact} of {n} oldString lines match lines {matchedStart}-{steps[^1].File + 1} exactly, "
                   + $"but its first {off} {noun} not in the file: before line {matchedStart} the file {before}. "
                   + $"Your oldString starts before the real text — drop or correct {pronoun}.";
        }

        return null;
    }

    private static string DescribeGaps(Alignment alignment)
    {
        var parts = new List<string>();
        if (alignment.Extra > 0) parts.Add($"has {Lines(alignment.Extra)} that the file doesn't have ([extra] — remove)");
        if (alignment.Missing > 0) parts.Add($"skips {Lines(alignment.Missing)} that the file has ([missing] — add back)");
        if (alignment.Diff > 0) parts.Add($"has {Lines(alignment.Diff)} with different content ([diff] — copy the file's text)");
        return $"Your oldString {string.Join("; ", parts)}. Keep the [ok] lines as they are.";

        static string Lines(int count) => count == 1 ? "1 line" : $"{count} lines";
    }

    /// <summary>
    /// 首空白差异的描述。尾空白已被 fuzzy 的 TrimEnd 吸收、走不到 NotFound，
    /// 这里能看到的空白差只剩行首（以及行首空格 vs 制表符）。
    /// </summary>
    private static string DescribeWhitespaceDiff(string expected, string found)
    {
        int expLeading = expected.Length - expected.TrimStart().Length;
        int fndLeading = found.Length - found.TrimStart().Length;

        return expLeading == fndLeading
            ? " — differs by whitespace only (spaces vs tabs)."
            : $" — differs by whitespace only: leading whitespace {expLeading} vs {fndLeading}.";
    }

    /// <summary>本地截断（不入依赖 ToolOutputTruncation.TruncateLine）：只为控制话术长度</summary>
    private static string Clamp(string s) => s.Length <= 120 ? s : s[..120] + " …[truncated]";

    #endregion

    /// <summary>
    /// Damerau-Levenshtein 编辑距离，上限 <paramref name="max"/> 即提前剪枝（返回 max+1）。
    /// 只需「够不够近」，不需要精确距离。
    /// </summary>
    private static int DamerauLevenshtein(string a, string b, int max = 60)
    {
        int n = a.Length;
        int m = b.Length;
        if (Math.Abs(n - m) > max) return max + 1;
        if (n == 0) return m;
        if (m == 0) return n;

        int[] prev = new int[m + 1];
        int[] curr = new int[m + 1];
        int[] prevPrev = new int[m + 1];
        for (int j = 0; j <= m; j++) { prev[j] = j; curr[j] = j; }

        for (int i = 1; i <= n; i++)
        {
            (prevPrev, prev, curr) = (prev, curr, prevPrev);
            curr[0] = i;
            int rowMin = i;
            for (int j = 1; j <= m; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                int val = Math.Min(prev[j] + 1, Math.Min(curr[j - 1] + 1, prev[j - 1] + cost));
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    val = Math.Min(val, prevPrev[j - 2] + 1);
                }

                curr[j] = val;
                if (val < rowMin) rowMin = val;
            }

            if (rowMin > max) return max + 1;
        }

        return curr[m];
    }

    /// <summary>oldString 每行与文件行的对照结果</summary>
    private enum EStep
    {
        Exact,
        Whitespace,
        Diff,
        Extra, //oldString 有、文件里没有
        Missing, //文件里有、oldString 漏了
    }

    /// <param name="Kind">对照结果</param>
    /// <param name="Anchor">oldString 行下标；Missing 为 -1</param>
    /// <param name="File">文件行下标；Extra 为 -1</param>
    private readonly record struct Step(EStep Kind, int Anchor, int File);

    private sealed class Alignment(List<Step> steps)
    {
        public List<Step> Steps { get; } = steps;
        public int Diff { get; } = steps.Count(s => s.Kind == EStep.Diff);
        public int Extra { get; } = steps.Count(s => s.Kind == EStep.Extra);
        public int Missing { get; } = steps.Count(s => s.Kind == EStep.Missing);
        public int ContentMatched => Steps.Count(s => s.Kind is EStep.Exact or EStep.Whitespace);
        public bool HasGap => Extra + Missing > 0;

        /// <summary>解释不了的行数：改写、多出、缺失各算一行</summary>
        public int Unmatched => Diff + Extra + Missing;
    }
}
