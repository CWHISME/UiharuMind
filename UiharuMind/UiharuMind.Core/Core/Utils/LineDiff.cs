/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.Core.Utils;

/// <summary>
/// diff 行的类别
/// </summary>
public enum ELineDiffKind
{
    /// <summary>未变化的上下文行</summary>
    Context,

    /// <summary>新增行</summary>
    Added,

    /// <summary>删除行</summary>
    Removed,
}

/// <summary>
/// 一条 diff 行
/// </summary>
/// <param name="Kind">类别</param>
/// <param name="Text">行内容</param>
/// <param name="LineNumber">
/// 1 起的行号（新增行取新文件侧，其余取旧文件侧）；0 表示未知。
/// <see cref="LineDiff.Compute"/> 只比较两段文本，算不出它们在文件里的位置，故一律留 0；
/// 由知道位置的调用方（<c>FileEditPlanner</c>）填。
/// </param>
public sealed record LineDiffEntry(ELineDiffKind Kind, string Text, int LineNumber = 0);

/// <summary>
/// 行级文本 diff（LCS）。服务编辑审批卡片的可读渲染——
/// 编辑工具的 old/new 通常只是变更区域附近的少量行，LCS 规模可控；
/// 超出上限时退化为"整块删除+整块新增"（可读性略降但语义不失真）。
/// </summary>
public static class LineDiff
{
    /// <summary>
    /// 计算两段文本的行级 diff
    /// </summary>
    /// <param name="oldText">旧文本</param>
    /// <param name="newText">新文本</param>
    /// <param name="maxLcsLines">两侧行数之和超过该值时退化为块级 diff</param>
    /// <returns>diff 行序列</returns>
    public static List<LineDiffEntry> Compute(string oldText, string newText, int maxLcsLines = 400)
    {
        string[] oldLines = SplitLines(oldText);
        string[] newLines = SplitLines(newText);

        if (oldLines.Length + newLines.Length > maxLcsLines)
        {
            List<LineDiffEntry> blocks = new(oldLines.Length + newLines.Length);
            blocks.AddRange(oldLines.Select(x => new LineDiffEntry(ELineDiffKind.Removed, x)));
            blocks.AddRange(newLines.Select(x => new LineDiffEntry(ELineDiffKind.Added, x)));
            return blocks;
        }

        // 标准 LCS 动态规划 + 回溯
        int[,] table = new int[oldLines.Length + 1, newLines.Length + 1];
        for (int i = oldLines.Length - 1; i >= 0; i--)
        {
            for (int j = newLines.Length - 1; j >= 0; j--)
            {
                table[i, j] = oldLines[i] == newLines[j]
                    ? table[i + 1, j + 1] + 1
                    : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        List<LineDiffEntry> entries = new();
        int oldIndex = 0;
        int newIndex = 0;
        while (oldIndex < oldLines.Length && newIndex < newLines.Length)
        {
            if (oldLines[oldIndex] == newLines[newIndex])
            {
                entries.Add(new LineDiffEntry(ELineDiffKind.Context, oldLines[oldIndex]));
                oldIndex++;
                newIndex++;
            }
            else if (table[oldIndex + 1, newIndex] >= table[oldIndex, newIndex + 1])
            {
                entries.Add(new LineDiffEntry(ELineDiffKind.Removed, oldLines[oldIndex]));
                oldIndex++;
            }
            else
            {
                entries.Add(new LineDiffEntry(ELineDiffKind.Added, newLines[newIndex]));
                newIndex++;
            }
        }

        while (oldIndex < oldLines.Length) entries.Add(new LineDiffEntry(ELineDiffKind.Removed, oldLines[oldIndex++]));
        while (newIndex < newLines.Length) entries.Add(new LineDiffEntry(ELineDiffKind.Added, newLines[newIndex++]));
        return entries;
    }

    private static string[] SplitLines(string text)
    {
        return string.IsNullOrEmpty(text) ? [] : text.Replace("\r\n", "\n").Split('\n');
    }
}
