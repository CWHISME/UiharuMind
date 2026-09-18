/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.ComponentModel;
using System.Text;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// 一处编辑：把文件里唯一出现的 <see cref="OldString"/> 换成 <see cref="NewString"/>。
/// 本类型直接作为 <c>Edit</c> 工具的参数形状，属性说明就是模型看到的 schema 文案。
/// </summary>
public sealed class FileEdit
{
    /// <summary>被替换的原文，必须在文件中唯一出现，且不得与同一次调用的其他编辑重叠</summary>
    [Description("Exact text to replace. Must occur exactly once in the file, and must not overlap "
                 + "another entry in the same call. Keep it as small as it can be while still unique.")]
    public string OldString { get; set; } = string.Empty;

    /// <summary>替换后的文本（空串表示删掉这段）</summary>
    [Description("Replacement text. An empty string deletes the matched text.")]
    public string NewString { get; set; } = string.Empty;
}

/// <summary>
/// 一次编辑的干跑结果：成功带新正文与 diff，失败带一句给模型的话术。
///
/// 工具执行与审批卡片预演拿到的是<b>同一个对象</b>，所以两边不可能对同一次调用说出不同的话，
/// 也不可能出现「卡片显示改得挺好、落盘却失败」。
/// </summary>
public sealed class FileEditPlan
{
    private FileEditPlan(string? error, string newText, IReadOnlyList<LineDiffEntry> diff,
        TextFileEnvelope envelope)
    {
        Error = error;
        NewText = newText;
        Diff = diff;
        Envelope = envelope;
    }

    /// <summary>失败原因（英文，直接进模型上下文，也直接显示在审批卡片上）；成功时为 null</summary>
    public string? Error { get; }

    /// <summary>是否可以落盘</summary>
    public bool Succeeded => Error is null;

    /// <summary>变更的行级 diff（带行号，按变更块聚合，块之间的上下文已合并）</summary>
    public IReadOnlyList<LineDiffEntry> Diff { get; }

    /// <summary>落盘用的新正文（原文行尾原样保留，只有新插入的文本按文件风格转写）</summary>
    internal string NewText { get; }

    /// <summary>读入时的落盘保真信封</summary>
    internal TextFileEnvelope Envelope { get; }

    internal static FileEditPlan Failed(string error) => new(error, string.Empty, [], default);

    internal static FileEditPlan Success(string newText, IReadOnlyList<LineDiffEntry> diff,
        TextFileEnvelope envelope) => new(null, newText, diff, envelope);
}

/// <summary>
/// 编辑语义的唯一定义处：一组编辑对着<b>原文</b>算出新正文与 diff，不落盘。
///
/// 匹配规则，按顺序：
/// <list type="number">
/// <item>精确匹配。命中多处即失败——要求模型自己加上下文，而不是替它猜哪一处。</item>
/// <item>保守 fuzzy：按<b>整行窗口</b>比对，逐行 <c>TrimEnd</c> 后相等即算命中。
/// 它同时吸收了「行尾多余空白」与「CRLF/LF 不一致」两类差异——后者是结构上被吸收的，
/// 因为切行时 <c>\r\n</c> 与 <c>\n</c> 都是终止符，不参与比较。</item>
/// <item>受控全角归一：仍不中时，把 <c>，。：；（）！？</c> 与全角空格映射成半角再比一次。
/// 模型常把中文注释里的全角标点抄成半角（或反之），这层吸收能救回这类失败。</item>
/// </list>
///
/// 归一的<b>边界</b>：只映射上面这份白名单标点——不碰智能引号、破折号、省略号，
/// 那些没有无语义冲突的半角对应（"→\" 会把注释里的引号与代码字符串混为一谈）。
/// 归一只用于<b>判定命中</b>，落盘仍按 newString；命中区外的字节永远不动，
/// 因此被归一救回的匹配不会把整段注释悄悄改写。不做 NFKC 全文归一（pi 的 edit-diff.ts 做了）：
/// NFKC 会把更多字符映射掉，且全角空格归一会让行首缩进比较变宽。见 ADR 0007。
///
/// 命中区间之外的字节永远不动，因此混用换行的文件也不会被统一。
/// </summary>
public static class FileEditPlanner
{
    /// <summary>diff 每个变更块上下各带几行上下文</summary>
    private const int ContextLines = 2;

    /// <summary>NotFound 候选提示最多评多少行的块:再大就放弃(避免评分随块行数变慢)</summary>
    private const int MaxHintBlockLines = 40;

    /// <summary>NotFound 候选提示最多列出多少行,超出折叠</summary>
    private const int MaxHintShownLines = 20;

    /// <summary>
    /// 从磁盘读文件并算一份编辑计划，不落盘
    /// </summary>
    /// <param name="absolutePath">文件绝对路径</param>
    /// <param name="label">出现在话术里的文件名（用模型给的那个写法，不是解析后的绝对路径）</param>
    /// <param name="edits">编辑清单</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>编辑计划</returns>
    public static async Task<FileEditPlan> PlanFileAsync(string absolutePath, string label,
        IReadOnlyList<FileEdit>? edits, CancellationToken ct = default)
    {
        if (!File.Exists(absolutePath)) return FileEditPlan.Failed($"File '{label}' not found.");

        byte[] bytes = await File.ReadAllBytesAsync(absolutePath, ct).ConfigureAwait(false);
        return Plan(TextFileEnvelope.FromBytes(bytes), label, edits);
    }

    /// <summary>
    /// 从磁盘读文件并算一份编辑计划，不落盘（同步版：审批卡片要在构造时就把 diff 摆出来）。
    /// 走的是与执行完全同一条路径（含 BOM 剥离），因此预演结论就是执行结论。
    /// </summary>
    /// <param name="absolutePath">文件绝对路径</param>
    /// <param name="label">出现在话术里的文件名</param>
    /// <param name="edits">编辑清单</param>
    /// <returns>编辑计划</returns>
    public static FileEditPlan PlanFile(string absolutePath, string label, IReadOnlyList<FileEdit>? edits)
    {
        if (!File.Exists(absolutePath)) return FileEditPlan.Failed($"File '{label}' not found.");

        return Plan(TextFileEnvelope.FromBytes(File.ReadAllBytes(absolutePath)), label, edits);
    }

    /// <summary>
    /// 对给定原文算一份编辑计划（不碰磁盘）
    /// </summary>
    /// <param name="originalText">原文</param>
    /// <param name="label">出现在话术里的文件名</param>
    /// <param name="edits">编辑清单</param>
    /// <returns>编辑计划</returns>
    public static FileEditPlan Plan(string originalText, string label, IReadOnlyList<FileEdit>? edits)
        => Plan(TextFileEnvelope.FromText(originalText), label, edits);

    internal static FileEditPlan Plan(TextFileEnvelope envelope, string label, IReadOnlyList<FileEdit>? edits)
    {
        if (edits is not { Count: > 0 }) return FileEditPlan.Failed("edits must contain at least one entry.");

        string text = envelope.Text;
        List<Line> lines = SplitLines(text);
        List<Match> matches = new(edits.Count);

        // 逐条定位。全部对着原文算，不是对着前几条的结果算——
        // 这条是模型最容易搞错的，纪律段与 schema 文案都明说了
        for (int i = 0; i < edits.Count; i++)
        {
            string oldString = edits[i].OldString ?? string.Empty;
            if (oldString.Length == 0) return FileEditPlan.Failed($"edits[{i}].oldString must not be empty.");

            Location located = Locate(text, lines, oldString);
            switch (located.Kind)
            {
                case ELocateResult.NotFound:
                    return FileEditPlan.Failed(
                        BuildNotFoundMessage(i, oldString, label, text, lines));

                case ELocateResult.NotUnique:
                    // 归一歧义:多个命中里至少一个仅靠全角归一才匹配上——模型不知道"这两处"其实
                    // 只在标点上有差异,会误以为真有俩一模一样的块。必须点破,否则它加上下文永远加不对。
                    return FileEditPlan.Failed(located.NormalizedAmbiguity
                        ? $"edits[{i}].oldString occurs {located.Count} times in '{label}' — "
                          + "some matches differ only in full-width punctuation. Read the file and "
                          + "add surrounding context that pins the exact one."
                        : $"edits[{i}].oldString occurs {located.Count} times in '{label}'. "
                          + "Add surrounding lines to it so that it matches exactly one place.");
            }

            matches.Add(new Match(i, located.Start, located.End,
                envelope.ConvertNewLines(edits[i].NewString ?? string.Empty)));
        }

        matches.Sort((a, b) => a.Start.CompareTo(b.Start));
        for (int i = 1; i < matches.Count; i++)
        {
            if (matches[i - 1].End <= matches[i].Start) continue;
            return FileEditPlan.Failed(
                $"edits[{matches[i - 1].Index}] and edits[{matches[i].Index}] overlap in '{label}'. "
                + "Merge them into a single entry.");
        }

        string newText = Apply(text, matches);
        if (newText == text)
        {
            return FileEditPlan.Failed(
                $"No change: the edits produced content identical to '{label}'.");
        }

        return FileEditPlan.Success(newText, BuildDiff(text, lines, matches), envelope);
    }

    /// <summary>
    /// 把 diff 渲染成给模型看的文本（`+`/`-`/空格 前缀 + 右对齐行号）
    /// </summary>
    /// <param name="diff">diff 行</param>
    /// <param name="maxLines">最多渲染多少行，超出只给条数</param>
    /// <returns>diff 文本</returns>
    public static string RenderDiff(IReadOnlyList<LineDiffEntry> diff, int maxLines)
    {
        if (diff.Count == 0) return string.Empty;

        int width = diff.Max(x => x.LineNumber).ToString().Length;
        StringBuilder sb = new();
        int shown = 0; //已输出行数(预算消耗)
        int omitted = 0; //被截掉的内容行数
        int omittedHunks = 0; //内容没显示完整的块数(预算耗尽连头都放不下的也算)
        int i = 0;

        while (i < diff.Count)
        {
            if (diff[i].Kind != ELineDiffKind.Hunk)
            {
                // 无块头的纯行(非本工具产物,例如测试直接构造的 diff):逐行截断
                if (shown < maxLines)
                {
                    AppendLine(diff[i]);
                    shown++;
                }
                else omitted++;

                i++;
                continue;
            }

            // 找到一个块的完整跨度:从本 hunk 头到下一个 hunk 头(不含)
            int end = i + 1;
            while (end < diff.Count && diff[end].Kind != ELineDiffKind.Hunk) end++;
            int contentLen = end - i - 1; //块内内容行数(不含头)

            if (shown + 1 + contentLen <= maxLines)
            {
                // 整块放得下:整体输出,块头与内容不分离
                for (int k = i; k < end; k++)
                {
                    AppendLine(diff[k]);
                    shown++;
                }
            }
            else if (shown < maxLines)
            {
                // 块放不下但还有预算:保留 hunk 头,再尽量给内容(从头开始,块内连续)。
                // 预算只剩 1 行时允许头-only(宁可空壳头提示"这里还有一块",也比整块消失好);
                // 除此之外尽力给内容,否则大块编辑(>80 行)时模型一行改动都看不到、自纠能力归零。
                sb.AppendLine(diff[i].Text);
                shown++;

                int canShow = Math.Min(contentLen, maxLines - shown);
                for (int k = 1; k <= canShow; k++)
                {
                    AppendLine(diff[i + k]);
                    shown++;
                }

                omitted += contentLen - canShow;
                if (omitted > 0) omittedHunks++;
            }
            else
            {
                // 预算已耗尽:这块整体放弃,连块头也算被省略的一行(否则折叠计数少算)
                omitted += contentLen + 1;
                omittedHunks++;
            }

            i = end;
        }

        if (omitted > 0)
        {
            sb.Append(omittedHunks > 0
                ? $"…(+{omitted} more diff lines across {omittedHunks} hunk(s); "
                  + "remaining hunks' coordinates are in the @@ headers above, use Read to inspect)"
                : $"…(+{omitted} more diff lines)");
        }

        return sb.ToString().TrimEnd('\n', '\r');

        void AppendLine(LineDiffEntry entry)
        {
            if (entry.Kind == ELineDiffKind.Hunk)
            {
                // 块头自带坐标，不套前缀/行号列，兼做块间分隔线
                sb.AppendLine(entry.Text);
                return;
            }

            char prefix = entry.Kind switch
            {
                ELineDiffKind.Added => '+',
                ELineDiffKind.Removed => '-',
                _ => ' ',
            };
            sb.Append(prefix).Append(entry.LineNumber.ToString().PadLeft(width)).Append(' ')
                .AppendLine(entry.Text);
        }
    }

    // ---- 定位 ----

    /// <summary>
    /// 组装 NotFound 话术:在标准提示后追加「最近的整块」候选,让模型一步修正而不是反复读抄。
    ///
    /// 块级滑窗而不是单行锚:oldString 按行切块、贴着文件逐窗比,取整块距离和最小的窗口。
    /// 单行级评分会把「内容全等、只差缩进」的行输给远处一句内容相近的注释(实机见过),
    /// 块级才回得到"我们俩都以为的同一个地方"。
    /// 空白差异单列(内容同=代价 0),并在差异行上把 expected/found 与空白数差写出来——
    /// 空白和内容是两种修法,混在一起说模型就只能重读。
    /// </summary>
    private static string BuildNotFoundMessage(int index, string oldString, string label,
        string text, List<Line> lines)
    {
        string message = $"edits[{index}].oldString was not found in '{label}'. It must match the file exactly, "
                         + "whitespace and line breaks included. Read the file again and copy the text from it.";

        string[] anchors = oldString.Split('\n');
        // 镜像 LocateByLineWindow:尾随换行产生的空末段只表示"连换行一起换",不参与评分
        if (anchors.Length > 1 && anchors[^1].Length == 0) anchors = anchors[..^1];
        // 超长行(算编辑距离不划算)与纯空块不做候选
        if (anchors.Length == 0 || anchors.Length > MaxHintBlockLines
            || anchors.Any(a => a.Length > 200) || anchors.All(a => a.Length == 0))
        {
            return message;
        }

        // 逐窗评分:内容同(Trim 后相等)→空白差,代价 0;否则按 TrimEnd 后的编辑距离计入。
        // 窗口代价单调不减,一旦不小于当前最优即可早停
        int bestStart = -1;
        int bestCost = int.MaxValue;
        var bestStates = new ELineMatchState[anchors.Length];
        for (int start = 0; start + anchors.Length <= lines.Count; start++)
        {
            int cost = 0;
            var states = new ELineMatchState[anchors.Length];
            for (int i = 0; i < anchors.Length; i++)
            {
                string candidate = LineText(text, lines[start + i]).TrimEnd();
                if (candidate.Length > 200)
                {
                    cost = int.MaxValue;
                    break;
                }

                string anchorTrimmed = anchors[i].TrimEnd();
                if (anchorTrimmed == candidate)
                {
                    states[i] = ELineMatchState.Exact;
                    continue;
                }

                if (anchorTrimmed.Trim() == candidate.Trim())
                {
                    states[i] = ELineMatchState.Whitespace;
                    continue;
                }

                states[i] = ELineMatchState.Diff;
                cost += DamerauLevenshtein(anchorTrimmed, candidate);
                if (cost >= bestCost) break; // 已不可能更好
            }

            if (cost >= bestCost) continue; // 同分保留更早的窗口
            bestCost = cost;
            bestStart = start;
            bestStates = states;
        }

        if (bestStart < 0) return message;

        // 阈值:整块距离和超过锚字符数一半才算"不够像"(近似,沿用单行口径,避免短行全相似误报)
        int anchorChars = anchors.Sum(a => a.TrimEnd().Length);
        if (bestCost > anchorChars / 2) return message;

        int contentMatched = bestStates.Count(s => s != ELineMatchState.Diff);
        int firstFix = Array.FindIndex(bestStates, s => s != ELineMatchState.Exact);

        var sb = new StringBuilder();
        sb.Append($" Closest match: line {bestStart + 1} ({contentMatched}/{anchors.Length} lines content-matched)");
        int shown = Math.Min(anchors.Length, MaxHintShownLines);
        for (int i = 0; i < shown; i++)
        {
            sb.Append($"\n   {bestStart + i + 1} [{KindLabel(bestStates[i])}] "
                      + Clamp(LineText(text, lines[bestStart + i]).TrimEnd()));
        }
        if (anchors.Length > shown)
        {
            sb.Append($"\n   …(+{anchors.Length - shown} more lines)");
        }

        if (firstFix >= 0)
        {
            string found = LineText(text, lines[bestStart + firstFix]).TrimEnd();
            sb.Append($"\nline {bestStart + firstFix + 1}: expected '{Clamp(anchors[firstFix].TrimEnd())}' "
                      + $"but found '{Clamp(found)}'");
            sb.Append(bestStates[firstFix] == ELineMatchState.Diff
                ? " — content differs."
                : DescribeWhitespaceDiff(anchors[firstFix].TrimEnd(), found));
        }

        return message + sb.ToString();

        static string KindLabel(ELineMatchState state) => state switch
        {
            ELineMatchState.Exact => "ok",
            ELineMatchState.Whitespace => "ws",
            _ => "diff",
        };
    }

    /// <summary>本地截断(不入依赖 PermissiveFileAccessTools.TruncateLine):只为控制话术长度</summary>
    private static string Clamp(string s) => s.Length <= 120 ? s : s[..120] + " …[truncated]";

    /// <summary>
    /// 首空白差异的描述。尾空白已被 fuzzy 的 TrimEnd 吸收、走不到 NotFound,
    /// 这里能看到的空白差只剩行首(以及行首空格 vs 制表符)。
    /// </summary>
    private static string DescribeWhitespaceDiff(string expected, string found)
    {
        int expLeading = expected.Length - expected.TrimStart().Length;
        int fndLeading = found.Length - found.TrimStart().Length;

        return expLeading == fndLeading
            ? " — differs by whitespace only (spaces vs tabs)."
            : $" — differs by whitespace only: leading whitespace {expLeading} vs {fndLeading}.";
    }

    /// <summary>
    /// Damerau-Levenshtein 编辑距离,上限 <paramref name="max"/> 即提前剪枝(返回 max+1)。
    /// 只需"够不够近",不需要精确距离。
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

        int rowMin = 0;
        for (int i = 1; i <= n; i++)
        {
            (prevPrev, prev, curr) = (prev, curr, prevPrev);
            curr[0] = i;
            rowMin = i;
            for (int j = 1; j <= m; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                int insert = prev[j] + 1;
                int delete = curr[j - 1] + 1;
                int sub = prev[j - 1] + cost;
                int val = Math.Min(insert, Math.Min(delete, sub));
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

    private static Location Locate(string text, List<Line> lines, string oldString)
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
    private static Location LocateByLineWindow(string text, List<Line> lines, string oldString)
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
                Line line = lines[i + j];
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

        Line last = lines[firstLine + keyCount - 1];
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

    // ---- 应用与 diff ----

    private static string Apply(string text, List<Match> matches)
    {
        StringBuilder sb = new(text.Length);
        int cursor = 0;
        foreach (Match match in matches)
        {
            sb.Append(text, cursor, match.Start - cursor);
            sb.Append(match.NewText);
            cursor = match.End;
        }

        sb.Append(text, cursor, text.Length - cursor);
        return sb.ToString();
    }

    /// <summary>
    /// 按变更块生成 diff。每块只对<b>该块覆盖的那几行</b>做 LCS——
    /// 整文件 LCS 既贵又会撞上 <see cref="LineDiff"/> 的降级阈值，而块的大小由模型给的
    /// oldString/newString 决定，天然可控。
    /// </summary>
    private static IReadOnlyList<LineDiffEntry> BuildDiff(string text, List<Line> lines, List<Match> matches)
    {
        List<LineDiffEntry> entries = new();
        int delta = 0; //新旧行号偏移，跨块累加
        int index = 0;

        while (index < matches.Count)
        {
            int startLine = LineIndexOf(lines, matches[index].Start);
            int endLine = LineIndexOf(lines, Math.Max(matches[index].Start, matches[index].End - 1));
            int last = index;
            int maxEnd = matches[index].End;

            // 相邻两块的上下文窗口若会重叠，就并成一块——否则同一行会在 diff 里出现两次
            while (last + 1 < matches.Count &&
                   LineIndexOf(lines, matches[last + 1].Start) - endLine <= ContextLines * 2)
            {
                last++;
                endLine = Math.Max(endLine,
                    LineIndexOf(lines, Math.Max(matches[last].Start, matches[last].End - 1)));
                maxEnd = Math.Max(maxEnd, matches[last].End);
            }

            int regionStart = lines[startLine].Start;
            int regionEnd = Math.Max(lines[endLine].ContentEnd, maxEnd);

            StringBuilder sb = new();
            int cursor = regionStart;
            for (int k = index; k <= last; k++)
            {
                sb.Append(text, cursor, matches[k].Start - cursor);
                sb.Append(matches[k].NewText);
                cursor = matches[k].End;
            }

            if (cursor < regionEnd) sb.Append(text, cursor, regionEnd - cursor);

            // 变更区（含块内上下文）行数：旧侧 = Removed + Context，新侧 = Added + Context。
            // 整块内旧新行数差恰好是 delta 的来源，这里直接算出来填 hunk 头，不再猜。
            List<LineDiffEntry> computed = LineDiff.Compute(
                TrimOneTrailingNewLine(text[regionStart..regionEnd]),
                TrimOneTrailingNewLine(sb.ToString()));
            int oldBlockLines = computed.Count(e => e.Kind is ELineDiffKind.Removed or ELineDiffKind.Context);
            int newBlockLines = computed.Count(e => e.Kind is ELineDiffKind.Added or ELineDiffKind.Context);

            // hunk 头：声明旧/新两套坐标（+ 有 delta 偏移），兼作块与块之间的分隔线。
            // 上下文行数前置/后置分别算，块头覆盖「前置上下文 + 变更区 + 后置上下文」整段。
            int preContextLines = startLine - Math.Max(0, startLine - ContextLines);
            int postContextLines = Math.Max(0,
                Math.Min(lines.Count, endLine + 1 + ContextLines) - (endLine + 1));
            int hunkOldStart = Math.Max(0, startLine - ContextLines) + 1;
            int hunkNewStart = hunkOldStart + delta;
            entries.Add(new LineDiffEntry(ELineDiffKind.Hunk,
                BuildHunkHeader(hunkOldStart, preContextLines + oldBlockLines + postContextLines,
                    hunkNewStart, preContextLines + newBlockLines + postContextLines)));

            // 前置上下文
            for (int c = Math.Max(0, startLine - ContextLines); c < startLine; c++)
            {
                entries.Add(new LineDiffEntry(ELineDiffKind.Context, LineText(text, lines[c]), c + 1));
            }

            int oldNo = startLine + 1;
            int newNo = oldNo + delta;
            foreach (LineDiffEntry entry in computed)
            {
                switch (entry.Kind)
                {
                    case ELineDiffKind.Added:
                        entries.Add(entry with { LineNumber = newNo++ });
                        break;
                    case ELineDiffKind.Removed:
                        entries.Add(entry with { LineNumber = oldNo++ });
                        break;
                    default:
                        entries.Add(entry with { LineNumber = oldNo++ });
                        newNo++;
                        break;
                }
            }

            delta = newNo - oldNo;

            // 后置上下文
            for (int c = endLine + 1; c < Math.Min(lines.Count, endLine + 1 + ContextLines); c++)
            {
                entries.Add(new LineDiffEntry(ELineDiffKind.Context, LineText(text, lines[c]), c + 1));
            }

            index = last + 1;
        }

        return entries;
    }

    /// <summary>
    /// 拼 unified diff 风格的块头。计数为 1 时省略（<c>-4</c> 而不是 <c>-4,1</c>），
    /// 与 git diff 的输出习惯一致，模型见到的是它熟悉的形状。
    /// 计数为 0（整块删光）时起始归 0（<c>+0,0</c>），也是 git 惯例。
    /// </summary>
    private static string BuildHunkHeader(int oldStart, int oldCount, int newStart, int newCount)
    {
        static string Range(int start, int count) => count switch
        {
            0 => "0,0",
            1 => $"{start}",
            _ => $"{start},{count}",
        };
        return $"@@ -{Range(oldStart, oldCount)} +{Range(newStart, newCount)} @@";
    }

    private static string TrimOneTrailingNewLine(string text)
    {
        if (text.EndsWith('\n')) text = text[..^1];
        return text.EndsWith('\r') ? text[..^1] : text;
    }

    private static string LineText(string text, Line line) => text[line.Start..line.ContentEnd];

    private static int LineIndexOf(List<Line> lines, int offset)
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

    /// <summary>
    /// 切行，保留每行在原文里的三个位置。<c>\r\n</c> 与 <c>\n</c> 都算终止符且不计入
    /// <see cref="Line.ContentEnd"/>，行尾比较因此天然对行尾风格不敏感。
    /// </summary>
    private static List<Line> SplitLines(string text)
    {
        List<Line> lines = new();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            int contentEnd = i > start && text[i - 1] == '\r' ? i - 1 : i;
            lines.Add(new Line(start, contentEnd, i + 1));
            start = i + 1;
        }

        if (start < text.Length) lines.Add(new Line(start, text.Length, text.Length));
        return lines;
    }

    private enum ELocateResult
    {
        Found,
        NotFound,
        NotUnique,
    }

    /// <summary>NotFound 候选里 oldString 每行与文件行的比对结果</summary>
    private enum ELineMatchState { Exact, Whitespace, Diff }

    /// <param name="Start">行首在原文里的偏移</param>
    /// <param name="ContentEnd">正文结束处（不含行终止符）</param>
    /// <param name="End">含行终止符的结束处</param>
    private readonly record struct Line(int Start, int ContentEnd, int End);

    private readonly record struct Location(ELocateResult Kind, int Start, int End, int Count,
        bool NormalizedAmbiguity = false);

    private readonly record struct Match(int Index, int Start, int End, string NewText);
}
