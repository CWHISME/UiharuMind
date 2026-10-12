using System;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 只读片段的行号口径：首行对应原文件第几行、前几行是原文。
/// Read 从中间读起时行号要对上原文件；结果末尾的截断附注、文件大纲不是原文，不该编上行号冒充文件内容。
/// <c>default</c> 等同 <see cref="Whole"/>：从 1 起、全部编号。
/// </summary>
/// <param name="First">首行对应的原文件行号</param>
/// <param name="Count">编号的行数（之后是附注，不编号）；0 表示全部编号</param>
public readonly record struct LineNumbering(int First, int Count = 0)
{
    /// <summary>整份文本：从 1 起、全部编号</summary>
    public static LineNumbering Whole => new(1);

    /// <summary>是否就是普通编号（从 1 起、全部编号），此时走上游默认渲染</summary>
    public bool IsWhole => FirstLine == 1 && Count == 0;

    /// <summary>首行行号，不小于 1</summary>
    public int FirstLine => Math.Max(1, First);

    /// <summary>
    /// 文档第几行显示的行号
    /// </summary>
    /// <param name="documentLine">文档行号，1 起算</param>
    /// <returns>显示的行号；附注行返回 null（不编号）</returns>
    public int? NumberOf(int documentLine) =>
        Count > 0 && documentLine > Count ? null : FirstLine + documentLine - 1;
}
