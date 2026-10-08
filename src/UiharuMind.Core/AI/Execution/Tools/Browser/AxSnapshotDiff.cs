/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Tools.Browser;

/// <summary>
/// 与上一份交给模型的快照比对。填字、勾选、选下拉这类动作只改动一两行，每次重发整页是纯开销；
/// 结构没变时编号也不变，所以只给变了的行就够模型接着用。
/// </summary>
public static class AxSnapshotDiff
{
    private const double MaxChangedFraction = 0.2; //变得比这多就不如整页重给

    /// <summary>
    /// 原地变了的行（各给新内容）
    /// </summary>
    /// <param name="previous">上一份快照</param>
    /// <param name="current">这一份快照</param>
    /// <returns>完全相同为空串；行数变了或变得太多（结构变了、编号可能错位）为 null，应整页重给</returns>
    public static string? ChangedLines(string previous, string current)
    {
        if (previous == current) return string.Empty;

        string[] before = previous.Split('\n');
        string[] after = current.Split('\n');
        if (before.Length != after.Length) return null;

        List<string> changed = new();
        for (int i = 0; i < after.Length; i++)
        {
            if (before[i] != after[i]) changed.Add(after[i]);
        }

        return changed.Count <= Math.Max(1, after.Length * MaxChangedFraction) ? string.Join('\n', changed) : null;
    }
}
