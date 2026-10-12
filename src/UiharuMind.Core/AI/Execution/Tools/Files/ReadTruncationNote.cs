/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Globalization;
using System.Text.RegularExpressions;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// Read 截断附注的唯一格式：工具这头写，界面那头读（全文窗据此让行号对上原文件）
/// </summary>
public static class ReadTruncationNote
{
    private const string Marker = "…[已截断：显示第 "; //附注开头；全文窗以它切开原文与附注

    private static readonly Regex ShownStart = new(@"\[已截断：显示第 (\d+)–",
        RegexOptions.Compiled | RegexOptions.RightToLeft);

    // 独占一行的完整附注；原文里恰好含附注字样时，靠「附注前正好是它声明的行数」排除
    private static readonly Regex NoteLine = new(@"^…\[已截断：显示第 (\d+)–(\d+) 行，[^\n]*；续读传 offset=\d+\]$",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.RightToLeft);

    /// <summary>
    /// 生成截断附注
    /// </summary>
    /// <param name="start">首行行号</param>
    /// <param name="end">末行行号</param>
    /// <param name="total">总量说明，如「共 2500 行」；流式读没数行数时是文件大小</param>
    /// <param name="next">续读用的 offset</param>
    /// <returns>附注正文（不含前导换行）</returns>
    public static string Format(int start, int end, string total, int next) =>
        $"{Marker}{start}–{end} 行，{total}；续读传 offset={next}]";

    /// <summary>
    /// 推算一次 Read 结果首行在原文件里的行号（正文还不带「行号→」前缀的旧会话用；新结果直接看 <see cref="ReadLineNumbers"/>）。有截断附注以附注为准（负 offset 也算得对）；
    /// 否则用正 offset；负 offset 又没截断时无从得知，退回 1
    /// </summary>
    /// <param name="offset">调用时的 offset 参数</param>
    /// <param name="resultText">Read 的结果原文</param>
    /// <returns>首行行号</returns>
    public static int FirstLineOf(int offset, string resultText)
    {
        Match match = ShownStart.Match(resultText);
        if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture,
                out int start))
        {
            return Math.Max(1, start);
        }

        return Math.Max(1, offset);
    }

    /// <summary>
    /// 一次 Read 结果里原文占前几行（同上，只为旧会话）。截断附注及其后的大纲、提示都不是原文，全文窗不给它们编行号
    /// </summary>
    /// <param name="resultText">Read 的结果原文</param>
    /// <returns>原文行数；没有截断附注时返回 0（整份都是原文）</returns>
    public static int ContentLineCount(string resultText)
    {
        foreach (Match match in NoteLine.Matches(resultText))
        {
            int start = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            int end = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            int linesBefore = resultText.AsSpan(0, match.Index).Count('\n');
            if (end >= start && linesBefore == end - start + 1) return linesBefore;
        }

        return 0;
    }
}
