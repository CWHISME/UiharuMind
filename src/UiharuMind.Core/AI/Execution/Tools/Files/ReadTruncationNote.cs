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
    private static readonly Regex ShownStart = new(@"\[truncated: showing lines (\d+)–",
        RegexOptions.Compiled | RegexOptions.RightToLeft);

    /// <summary>
    /// 生成截断附注
    /// </summary>
    /// <param name="start">首行行号</param>
    /// <param name="end">末行行号</param>
    /// <param name="total">总行数（流式读时可能是「≥N」这类估计）</param>
    /// <param name="next">续读用的 offset</param>
    /// <returns>附注正文（不含前导换行）</returns>
    public static string Format(int start, int end, string total, int next) =>
        $"…[truncated: showing lines {start}–{end} of {total}; continue with offset={next}]";

    /// <summary>
    /// 推算一次 Read 结果首行在原文件里的行号。有截断附注以附注为准（负 offset 也算得对）；
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
}
