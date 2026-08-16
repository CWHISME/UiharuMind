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
/// </list>
///
/// 刻意<b>不做</b> NFKC / 全角标点 / 智能引号归一（pi 的 edit-diff.ts 做了）：NFKC 会把
/// <c>（）：，</c> 映射成 ASCII 半角，而本仓注释通篇是带全角标点的中文——一次 fuzzy 命中
/// 就会把注释悄悄改写。见 ADR 0007。
///
/// 命中区间之外的字节永远不动，因此混用换行的文件也不会被统一。
/// </summary>
public static class FileEditPlanner
{
    /// <summary>diff 每个变更块上下各带几行上下文</summary>
    private const int ContextLines = 2;

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
                        $"edits[{i}].oldString was not found in '{label}'. It must match the file exactly, "
                        + "whitespace and line breaks included. Read the file again and copy the text from it.");

                case ELocateResult.NotUnique:
                    return FileEditPlan.Failed(
                        $"edits[{i}].oldString occurs {located.Count} times in '{label}'. "
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
        int shown = Math.Min(diff.Count, maxLines);
        for (int i = 0; i < shown; i++)
        {
            LineDiffEntry entry = diff[i];
            char prefix = entry.Kind switch
            {
                ELineDiffKind.Added => '+',
                ELineDiffKind.Removed => '-',
                _ => ' ',
            };
            sb.Append(prefix).Append(entry.LineNumber.ToString().PadLeft(width)).Append(' ')
                .AppendLine(entry.Text);
        }

        if (diff.Count > shown) sb.Append($"…(+{diff.Count - shown} more diff lines)");
        return sb.ToString().TrimEnd('\n', '\r');
    }

    // ---- 定位 ----

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
        for (int i = 0; i + keyCount <= lines.Count; i++)
        {
            bool hit = true;
            for (int j = 0; j < keyCount; j++)
            {
                Line line = lines[i + j];
                if (text.AsSpan(line.Start, line.ContentEnd - line.Start).TrimEnd()
                    .SequenceEqual(pieces[j].AsSpan().TrimEnd())) continue;

                hit = false;
                break;
            }

            if (!hit) continue;
            if (firstLine < 0) firstLine = i;
            count++;
        }

        if (count == 0) return new Location(ELocateResult.NotFound, -1, -1, 0);
        if (count > 1) return new Location(ELocateResult.NotUnique, -1, -1, count);

        Line last = lines[firstLine + keyCount - 1];
        return new Location(ELocateResult.Found, lines[firstLine].Start,
            consumesTerminator ? last.End : last.ContentEnd, 1);
    }

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

            // 前置上下文
            for (int c = Math.Max(0, startLine - ContextLines); c < startLine; c++)
            {
                entries.Add(new LineDiffEntry(ELineDiffKind.Context, LineText(text, lines[c]), c + 1));
            }

            int oldNo = startLine + 1;
            int newNo = oldNo + delta;
            foreach (LineDiffEntry entry in LineDiff.Compute(
                         TrimOneTrailingNewLine(text[regionStart..regionEnd]),
                         TrimOneTrailingNewLine(sb.ToString())))
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

    /// <param name="Start">行首在原文里的偏移</param>
    /// <param name="ContentEnd">正文结束处（不含行终止符）</param>
    /// <param name="End">含行终止符的结束处</param>
    private readonly record struct Line(int Start, int ContentEnd, int End);

    private readonly record struct Location(ELocateResult Kind, int Start, int End, int Count);

    private readonly record struct Match(int Index, int Start, int End, string NewText);
}
