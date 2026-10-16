/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Execution.Files;

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
    /// <summary>Edit 回给模型的 diff 行数上限（模型侧与审批卡片共用同一个数，见 <c>DiffLineView.BuildEditDiff</c>）</summary>
    public const int DefaultMaxDiffLines = 80;

    /// <summary>diff 单行长度上限，超出截断：minified JSON 之类一行可达几十 KB，行数上限拦不住它</summary>
    public const int DefaultMaxDiffLineChars = 240;

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
        if (!File.Exists(absolutePath)) return FileEditPlan.Failed($"找不到文件 '{label}'。");

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
        if (!File.Exists(absolutePath)) return FileEditPlan.Failed($"找不到文件 '{label}'。");

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
        if (edits is not { Count: > 0 }) return FileEditPlan.Failed("edits 至少要有一条。");

        // 坏字节解码时已成了替换字符，写回去就把没碰到的部分也改了
        if (!envelope.IsLossless)
        {
            return FileEditPlan.Failed(
                $"'{label}' 无法安全编辑：它的字节按 {envelope.EncodingName} 解码不干净（编码未知或混用），"
                + "保存时会把你没改的部分也一起改掉。");
        }

        string text = envelope.Text;
        List<TextLine> lines = TextLines.Split(text);
        List<EditMatch> matches = new(edits.Count);
        List<SkippedEdit> skipped = [];

        // 逐条定位，全部对着原文算，不是对着前几条的结果算。这是部分写入的地基（ADR 0087）：
        // 加上下面的重叠检测，没生效那条的 oldString 在写入后的文件里仍原样在，重发照旧命中。
        // 改成顺序定位的话，没生效的条目可能已被前面的条目改掉
        bool strippedPrefixes = false;
        for (int i = 0; i < edits.Count; i++)
        {
            string oldString = edits[i].OldString ?? string.Empty;
            string newString = edits[i].NewString ?? string.Empty;

            if (oldString.Length == 0)
            {
                skipped.Add(new SkippedEdit(i, oldString, null));
                continue;
            }

            Location located = EditLocator.Locate(text, lines, oldString);

            // 原样找不到，再看是不是连 Read 的行号前缀一起抄了过来：原文优先，真以行号前缀开头的内容不会被剥
            if (located.Kind == ELocateResult.NotFound && ReadLineNumbers.TryStripCopied(oldString, out string bareOld))
            {
                Location bare = EditLocator.Locate(text, lines, bareOld);
                if (bare.Kind != ELocateResult.NotFound)
                {
                    located = bare;
                    oldString = bareOld;
                    newString = ReadLineNumbers.StripEachCopied(newString);
                    strippedPrefixes = true;
                }
            }

            // 删了行号却留着其后的制表符：每行多一个行首 \t。newString 也每行都带才剥，混着带的多半是新写的行，不动
            if (located.Kind == ELocateResult.NotFound && ReadLineNumbers.TryStripCopiedSeparator(oldString, out string untabbedOld))
            {
                Location untabbed = EditLocator.Locate(text, lines, untabbedOld);
                if (untabbed.Kind != ELocateResult.NotFound)
                {
                    located = untabbed;
                    oldString = untabbedOld;
                    if (ReadLineNumbers.TryStripCopiedSeparator(newString, out string untabbedNew)) newString = untabbedNew;
                    strippedPrefixes = true;
                }
            }

            // 找不到、多处命中只属于这一条，其余照写；失败话术等写入后对着新文本出，行号才与磁盘对得上
            if (located.Kind != ELocateResult.Found)
            {
                skipped.Add(new SkippedEdit(i, oldString, located));
                continue;
            }

            matches.Add(new EditMatch(i, located.Start, located.End,
                envelope.ConvertNewLines(newString)));
        }

        if (matches.Count == 0) return FileEditPlan.Failed(DescribeSkipped(skipped, text, lines, label));

        // 重叠、存不下的字符归不到某一条，仍整批不写
        matches.Sort((a, b) => a.Start.CompareTo(b.Start));
        for (int i = 1; i < matches.Count; i++)
        {
            if (matches[i - 1].End <= matches[i].Start) continue;
            return FileEditPlan.Failed(
                $"edits[{matches[i - 1].Index}] 与 edits[{matches[i].Index}] 在 '{label}' 中重叠。"
                + "把它们合并成一条。");
        }

        string newText = Apply(text, matches);
        if (newText == text)
        {
            return FileEditPlan.Failed(skipped.Count > 0
                ? DescribeSkipped(skipped, text, lines, label)
                : $"没有改动：这些编辑后的内容与 '{label}' 原文完全相同。");
        }

        // 改一小段不该顺带换掉整个文件的编码，写不下就让模型换字符
        if (!envelope.CanEncode(newText))
        {
            return FileEditPlan.Failed(
                $"新文本里有 {envelope.EncodingName} 存不下的字符，而 '{label}' 是按 {envelope.EncodingName} 保存的。"
                + "请改用它支持的字符。");
        }

        string? skippedNote = skipped.Count == 0 ? null : DescribeSkipped(skipped, newText, TextLines.Split(newText), label);
        return FileEditPlan.Success(newText, EditDiffBuilder.Build(text, lines, matches), envelope, strippedPrefixes,
            edits.Count, matches.Count, skippedNote, skipped.Select(edit => edit.Index).ToList());
    }

    /// <summary>没生效的一条：原话术所需的 oldString 与定位结果；oldString 为空时 Located 为 null</summary>
    private readonly record struct SkippedEdit(int Index, string OldString, Location? Located);

    // 没生效条目的话术，对着 text 出（部分写入时是写入后的新文本）
    private static string DescribeSkipped(List<SkippedEdit> skipped, string text, List<TextLine> lines, string label)
    {
        string[]? fileLines = null;
        return string.Join("\n\n", skipped.Select(edit =>
        {
            if (edit.Located is null)
            {
                return $"edits[{edit.Index}].oldString 不能为空。`{FileToolNames.Edit}` 替换的是已有文本：oldString 必须是文件里确实存在的一段。"
                       + $"要追加到文件末尾，先 `{FileToolNames.Read}` 文件的最后几行，拿末尾那段作 oldString。";
            }

            Location now = EditLocator.Locate(text, lines, edit.OldString);
            if (now.Kind == ELocateResult.NotUnique) return DescribeNotUnique(edit.Index, now, label);
            if (now.Kind == ELocateResult.Found)
            {
                // 其余条目写入后它恰好唯一了：原样重发就能命中
                return $"edits[{edit.Index}] 对着原文定位不上（{(edit.Located.Value.Kind == ELocateResult.NotUnique ? $"出现了 {edit.Located.Value.Count} 次" : "没有找到")}），未生效；"
                       + "其余编辑写入后它在文件里恰好只有一处，原样重发即可。";
            }

            fileLines ??= lines.Select(line => TextLines.Text(text, line)).ToArray();
            return EditMissHint.Build(edit.Index, edit.OldString, label, fileLines);
        }));
    }

    // 归一歧义:多个命中里至少一个仅靠全角归一才匹配上——模型不知道"这两处"其实
    // 只在标点上有差异,会误以为真有俩一模一样的块。必须点破,否则它加上下文永远加不对
    private static string DescribeNotUnique(int index, Location located, string label)
    {
        string where = located.Lines is { Count: > 0 } starts ? $"（起始于第 {string.Join("、", starts)} 行）" : "";
        return located.NormalizedAmbiguity
            ? $"edits[{index}].oldString 在 '{label}' 中出现了 {located.Count} 次{where}——"
              + $"其中有几处只在全角/半角标点上不同。先 `{FileToolNames.Read}` 这几行，"
              + "再补上能锁定唯一那一处的上下文。"
            : $"edits[{index}].oldString 在 '{label}' 中出现了 {located.Count} 次{where}。"
              + "给它补上前后几行，让它只匹配一处。";
    }

    /// <summary>
    /// 把 diff 渲染成给模型看的文本，压缩规则见 <see cref="EditDiffRenderer"/>
    /// </summary>
    /// <param name="diff">diff 行</param>
    /// <param name="maxLines">最多渲染多少行，超出只给条数</param>
    /// <param name="maxLineChars">单行长度上限，超出截断加 <c>…[已截断]</c></param>
    /// <returns>diff 文本</returns>
    public static string RenderDiff(IReadOnlyList<LineDiffEntry> diff, int maxLines,
        int maxLineChars = DefaultMaxDiffLineChars)
        => EditDiffRenderer.Render(diff, maxLines, maxLineChars);

    private static string Apply(string text, List<EditMatch> matches)
    {
        StringBuilder sb = new(text.Length);
        int cursor = 0;
        foreach (EditMatch match in matches)
        {
            sb.Append(text, cursor, match.Start - cursor);
            sb.Append(match.NewText);
            cursor = match.End;
        }

        sb.Append(text, cursor, text.Length - cursor);
        return sb.ToString();
    }
}
