/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.RegularExpressions;

namespace UiharuMind.Core.AI.Execution.Tools.Browser;

/// <summary>
/// 正文交给模型前再收一道：链接只留字、空白行并成段落间距。
/// 维基这类站点的链接是百分号编码的相对地址，一个就几十字节，收掉后 Hacker News 首页少六成。
/// 要跟链接按字点就行——<c>BrowserAct</c> 认看得见的文字。代码（行内与围栏）原样保留：<c>d[k](x)</c> 不是链接
/// </summary>
internal static partial class ReadableText
{
    /// <summary>
    /// 收拾一份 Markdown 正文
    /// </summary>
    /// <param name="markdown">正文</param>
    /// <returns>收拾后的正文</returns>
    public static string Tidy(string markdown)
    {
        string text = LinkOrCodePattern().Replace(markdown,
            m => m.Groups["code"].Success ? m.Value : m.Groups["text"].Value);
        return BlankLinesPattern().Replace(text, "\n\n").Trim();
    }

    // 先认代码（围栏、行内），认到就原样放过；否则是链接或图片，只留方括号里的字。
    // 链接文字里可以再套一层方括号（维基的脚注 [[17]](#cite_note-17)），地址里可以再套一层圆括号（/wiki/Foo_(bar)）；
    // 都不跨行，没配对的方括号（区间写法 [0, 1)）扫到行尾就停
    [GeneratedRegex("""(?<code>```[\s\S]*?```|`[^`\n]*`)|!?\[(?<text>(?:[^\[\]\n]|\[[^\[\]\n]*\])*)\]\((?:[^()\s]|\([^()\s]*\))*(?:\s+"[^"\n]*")?\)""")]
    private static partial Regex LinkOrCodePattern();

    [GeneratedRegex(@"\n(?:[ \t]*\n)+")]
    private static partial Regex BlankLinesPattern();
}
