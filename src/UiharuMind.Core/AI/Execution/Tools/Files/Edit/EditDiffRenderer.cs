using System.Text;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>把编辑 diff 渲成给模型看的文本：折叠 context、按 hunk 均分行数预算</summary>
internal static class EditDiffRenderer
{
    /// <summary>每个 hunk 头尾各保留的定位锚行数，与数据层 <see cref="EditDiffBuilder.ContextLines"/> 保持一致，防止两层漂移</summary>
    private const int AnchorLines = EditDiffBuilder.ContextLines;

    /// <summary>无 hunk 头的纯 context（非本工具产物）超过该值才折叠；hunk 内不按它判，见 <see cref="CollapseContextRuns"/></summary>
    private const int CollapseContextThreshold = 4;

    /// <summary>hunk 内容超预算时头尾各保留的行数（尾部常是改动真正的落点）</summary>
    private const int SkeletonEdgeLines = 6;

    /// <summary>
    /// 把 diff 渲染成给模型看的文本（`+`/`-`/空格 前缀 + 右对齐行号）。
    /// 与 Read 不同仍右对齐：diff 只有几十行，补位代价可忽略，红绿行的正文列对齐才好比对。
    ///
    /// 两层压缩都只发生在渲染层，plan.Diff 保持完整数据：
    /// <list type="number">
    /// <item>连续的大段 context 行折叠成一行摘要。这些行来自 oldString/newString 里相同的行——
    /// 模型自己刚在参数里发过，原样返回纯属双倍浪费，还会挤掉真正的红绿行。</item>
    /// <item>预算按 hunk 均分，超预算的 hunk 给头尾骨架（头部 + 折叠行 + 尾部）。
    /// 尾部常常是改动真正的落点，只给头模型自纠能力打折。</item>
    /// </list>
    /// </summary>
    /// <param name="diff">diff 行</param>
    /// <param name="maxLines">最多渲染多少行，超出只给条数</param>
    /// <param name="maxLineChars">单行长度上限，超出截断加 <c>…[已截断]</c></param>
    /// <returns>diff 文本</returns>
    internal static string Render(IReadOnlyList<LineDiffEntry> diff, int maxLines, int maxLineChars)
    {
        if (diff.Count == 0) return string.Empty;

        List<LineDiffEntry> items = CollapseContextRuns(diff);
        int width = items.Max(x => x.LineNumber).ToString().Length;
        StringBuilder sb = new();
        int shown = 0; //已输出行数(预算消耗)
        int omitted = 0; //被截掉的内容行数
        int omittedHunks = 0; //内容没显示完整的块数(预算耗尽连头都放不下的也算)
        int i = 0;

        // hunk 间均分预算：每块配额 = 剩余预算 / 剩余块数。小块用不完的配额顺延给后面的块，
        // 既防第一个大块吃光后面全饿死，也不浪费。
        int remainingBudget = maxLines;
        int remainingHunks = items.Count(x => x.Kind == ELineDiffKind.Hunk);

        while (i < items.Count)
        {
            if (items[i].Kind != ELineDiffKind.Hunk)
            {
                // 无块头的纯行(非本工具产物,例如测试直接构造的 diff):逐行截断
                if (shown < maxLines)
                {
                    AppendLine(items[i], width, maxLineChars);
                    shown++;
                }
                else omitted++;

                i++;
                continue;
            }

            // 找到一个块的完整跨度:从本 hunk 头到下一个 hunk 头(不含)
            int end = i + 1;
            while (end < items.Count && items[end].Kind != ELineDiffKind.Hunk) end++;
            int contentLen = end - i - 1; //块内内容行数(不含头,折叠后)
            int budget = remainingHunks > 0 ? remainingBudget / remainingHunks : remainingBudget;
            int contentBudget = Math.Max(0, budget - 1); //hunk 头占 1 行
            int blockShown = 0;

            if (contentLen <= contentBudget)
            {
                // 整块放得下:整体输出,块头与内容不分离
                AppendLine(items[i], width, maxLineChars);
                blockShown++;
                for (int k = i + 1; k < end; k++)
                {
                    AppendLine(items[k], width, maxLineChars);
                    blockShown++;
                }
            }
            else if (contentBudget > 0)
            {
                // 内容超预算:保留 hunk 头,内容行按优先级挑选——红绿行与折叠行必保
                // (模型要看的就是改了什么),context 锚定行只补剩余预算。
                // 必保行本身就超预算时才对必保行做头尾骨架(尾部常是改动真正的落点),context 全部让位。
                AppendLine(items[i], width, maxLineChars);
                blockShown++;

                List<LineDiffEntry> content = new(contentLen);
                int mustKeep = 0;
                for (int k = i + 1; k < end; k++)
                {
                    content.Add(items[k]);
                    if (IsChangeLine(items[k])) mustKeep++;
                }

                int blockOmitted = 0;
                if (mustKeep <= contentBudget)
                {
                    int ctxBudget = contentBudget - mustKeep;
                    int ctxGiven = 0;
                    foreach (LineDiffEntry entry in content)
                    {
                        if (IsChangeLine(entry))
                        {
                            AppendLine(entry, width, maxLineChars);
                            blockShown++;
                        }
                        else if (ctxGiven < ctxBudget)
                        {
                            AppendLine(entry, width, maxLineChars);
                            blockShown++;
                            ctxGiven++;
                        }
                        else
                        {
                            blockOmitted++;
                        }
                    }
                }
                else
                {
                    // 必保行本身就超预算:对必保行做头尾骨架,context 全让位
                    List<LineDiffEntry> mustKeepLines = content.Where(IsChangeLine).ToList();
                    int head = Math.Min(SkeletonEdgeLines, contentBudget / 2);
                    int tail = Math.Min(SkeletonEdgeLines, contentBudget - head);
                    int canHead = Math.Min(head, mustKeepLines.Count);
                    int canTail = Math.Min(tail, mustKeepLines.Count - canHead);
                    int middle = mustKeepLines.Count - canHead - canTail;

                    for (int k = 0; k < canHead; k++)
                    {
                        AppendLine(mustKeepLines[k], width, maxLineChars);
                        blockShown++;
                    }
                    if (middle > 0)
                    {
                        sb.AppendLine($"…（省略 {middle} 行）…");
                        blockShown++; //折叠行也占预算,否则多个骨架块会让总行数悄悄超出上限
                    }
                    blockOmitted = middle + content.Count - mustKeepLines.Count;
                    for (int k = mustKeepLines.Count - canTail; k < mustKeepLines.Count; k++)
                    {
                        AppendLine(mustKeepLines[k], width, maxLineChars);
                        blockShown++;
                    }
                }

                omitted += blockOmitted;
                if (blockOmitted > 0) omittedHunks++;
            }
            else
            {
                // 预算只剩 hunk 头(或已耗尽):块头照给,内容整体放弃
                if (shown < maxLines)
                {
                    AppendLine(items[i], width, maxLineChars);
                    blockShown++;
                    omitted += contentLen;
                    omittedHunks++;
                }
                else
                {
                    omitted += contentLen + 1;
                    omittedHunks++;
                }
            }

            shown += blockShown;
            remainingBudget = Math.Max(0, remainingBudget - blockShown);
            remainingHunks--;
            i = end;
        }

        if (omitted > 0)
        {
            sb.Append(omittedHunks > 0
                ? $"…（另有 {omitted} 行差异未显示，涉及 {omittedHunks} 个变更块；"
                  + $"各块坐标见上方 @@ 头，需要时用 `{FileToolNames.Read}` 查看）"
                : $"…（另有 {omitted} 行差异未显示）");
        }

        return sb.ToString().TrimEnd('\n', '\r');

        // 红绿行与折叠行:预算不足时最先保住的一档(模型要看的就是改了什么)
        static bool IsChangeLine(LineDiffEntry entry)
            => entry.Kind is ELineDiffKind.Added or ELineDiffKind.Removed or ELineDiffKind.Collapsed;

        void AppendLine(LineDiffEntry entry, int lineWidth, int lineChars)
        {
            if (entry.Kind is ELineDiffKind.Hunk or ELineDiffKind.Collapsed)
            {
                // 块头与折叠行自带坐标，不套前缀/行号列，兼做块间分隔线
                sb.AppendLine(entry.Text);
                return;
            }

            char prefix = entry.Kind switch
            {
                ELineDiffKind.Added => '+',
                ELineDiffKind.Removed => '-',
                _ => ' ',
            };
            sb.Append(prefix).Append(entry.LineNumber.ToString().PadLeft(lineWidth)).Append(' ')
                .AppendLine(ClampDiffLine(entry.Text, lineChars));
        }
    }

    /// <summary>
    /// 按 hunk 折叠 context 行：每个 hunk 只保留头尾各 <see cref="AnchorLines"/> 行定位锚，中间的 context
    /// （oldString/newString 里相同的行）不管多短都折叠成一行摘要。头尾锚是模型核对「改动落在哪里」的参照，
    /// 中间的行模型自己刚在参数里发过，原样返回纯属双倍浪费。只有 1 行可折时不折（省不了行数还丢原文）。
    /// 无 hunk 头的 context（非本工具产物，如测试直接构造）超过 <see cref="CollapseContextThreshold"/> 才折叠。
    /// </summary>
    private static List<LineDiffEntry> CollapseContextRuns(IReadOnlyList<LineDiffEntry> diff)
    {
        List<LineDiffEntry> items = new(diff.Count);
        bool hasHunk = diff.Any(x => x.Kind == ELineDiffKind.Hunk);
        int i = 0;
        while (i < diff.Count)
        {
            if (diff[i].Kind != ELineDiffKind.Context)
            {
                items.Add(diff[i]);
                i++;
                continue;
            }

            int runStart = i;
            while (i < diff.Count && diff[i].Kind == ELineDiffKind.Context) i++;
            int len = i - runStart;

            if (!hasHunk)
            {
                // 无 hunk 头的纯 context(非本工具产物,如测试直接构造):沿用旧的 > 阈值折叠
                if (len <= CollapseContextThreshold)
                {
                    for (int k = runStart; k < i; k++) items.Add(diff[k]);
                }
                else
                {
                    items.Add(BuildCollapsed(diff, runStart, i));
                }

                continue;
            }

            // hunk 内:只保留头尾定位锚,中间不管多短都折叠
            bool firstInHunk = runStart > 0 && diff[runStart - 1].Kind == ELineDiffKind.Hunk;
            bool lastInHunk = i < diff.Count
                ? diff[i].Kind == ELineDiffKind.Hunk
                : hasHunk; //到 diff 末尾:属于最后一个 hunk 的尾部

            if (!firstInHunk && !lastInHunk)
            {
                // 块中部:整段折叠(1 行不值得折)
                if (len >= 2) items.Add(BuildCollapsed(diff, runStart, i));
                else items.Add(diff[runStart]);
                continue;
            }

            int head = firstInHunk ? Math.Min(AnchorLines, len) : 0;
            int tail = lastInHunk ? Math.Min(AnchorLines, len - head) : 0;
            int middle = len - head - tail;
            if (middle < 2)
            {
                // 只剩 1 行可折:不值得(省不了行数还丢原文),整体保留
                for (int k = runStart; k < i; k++) items.Add(diff[k]);
                continue;
            }

            for (int k = runStart; k < runStart + head; k++) items.Add(diff[k]);
            items.Add(BuildCollapsed(diff, runStart + head, i - tail));
            for (int k = i - tail; k < i; k++) items.Add(diff[k]);
        }

        return items;
    }

    /// <summary>折叠行：文本自带行号范围，行号列留 0（不参与宽度计算）。
    /// 尾部明说「这些行就是模型自己发的 oldString 内容」——模型不需要重读文件来确认它们没变</summary>
    private static LineDiffEntry BuildCollapsed(IReadOnlyList<LineDiffEntry> diff, int start, int end)
    {
        int firstLine = diff[start].LineNumber;
        int lastLine = diff[end - 1].LineNumber;
        return new LineDiffEntry(ELineDiffKind.Collapsed,
            $"…（第 {firstLine}-{lastLine} 行，共 {end - start} 行未改动，与你的 oldString 相同）…");
    }

    /// <summary>diff 单行截断(与 Read/Grep 的 TruncateLine 同款口吻):minified JSON 一行可达几十 KB,行数上限拦不住它</summary>
    private static string ClampDiffLine(string text, int maxChars)
        => text.Length <= maxChars ? text : text[..maxChars] + " …[已截断]";
}
