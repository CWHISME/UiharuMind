using System.Text;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>一组已定位的编辑对着原文生成行级 diff（带 hunk 头与上下文）</summary>
internal static class EditDiffBuilder
{
    /// <summary>diff 每个变更块上下各带几行上下文（也是渲染层保留的定位锚行数）</summary>
    internal const int ContextLines = 3;

    /// <summary>
    /// 按变更块生成 diff。每块只对<b>该块覆盖的那几行</b>做 LCS——
    /// 整文件 LCS 既贵又会撞上 <see cref="LineDiff"/> 的降级阈值，而块的大小由模型给的
    /// oldString/newString 决定，天然可控。
    /// </summary>
    internal static IReadOnlyList<LineDiffEntry> Build(string text, List<TextLine> lines, List<EditMatch> matches)
    {
        List<LineDiffEntry> entries = new();
        int delta = 0; //新旧行号偏移，跨块累加
        int index = 0;

        while (index < matches.Count)
        {
            int startLine = EditTextLines.IndexOf(lines, matches[index].Start);
            int endLine = EditTextLines.IndexOf(lines, Math.Max(matches[index].Start, matches[index].End - 1));
            int last = index;
            int maxEnd = matches[index].End;

            // 相邻两块的上下文窗口若会重叠，就并成一块——否则同一行会在 diff 里出现两次
            while (last + 1 < matches.Count &&
                   EditTextLines.IndexOf(lines, matches[last + 1].Start) - endLine <= ContextLines * 2)
            {
                last++;
                endLine = Math.Max(endLine,
                    EditTextLines.IndexOf(lines, Math.Max(matches[last].Start, matches[last].End - 1)));
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
                entries.Add(new LineDiffEntry(ELineDiffKind.Context, EditTextLines.Text(text, lines[c]), c + 1));
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
                entries.Add(new LineDiffEntry(ELineDiffKind.Context, EditTextLines.Text(text, lines[c]), c + 1));
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
}
