using System.Net;
using System.Text.RegularExpressions;

namespace UiharuMind.Core.Core.Utils;

/// <summary>
/// 把夹着 HTML 的 markdown（模型卡、GitHub 发布说明）整理成渲染器吃得下的 markdown：
/// 去掉 HTML 注释，代码块以外的 HTML 化成 markdown（链接、标题、列表、粗斜体保留，图片徽标与排版标签丢掉）
/// </summary>
public static partial class MarkdownCleaner
{
    /// <summary>
    /// 整理
    /// </summary>
    /// <param name="markdown">原文（也可以是纯 HTML）</param>
    /// <returns>整理后的 markdown</returns>
    public static string Clean(string markdown)
    {
        string text = markdown.Replace("\r\n", "\n");
        text = HtmlCommentPattern().Replace(text, "");
        text = SimplifyHtml(text);
        return BlankLinesPattern().Replace(text, "\n\n").Trim();
    }

    // 逐行走：围栏代码原样；以标签开头的连续行是 HTML 块，整块转成段落；其余行只转行内标签
    private static string SimplifyHtml(string text)
    {
        string[] lines = text.Split('\n');
        List<string> output = [];
        bool inFence = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                output.Add(line);
                continue;
            }

            if (inFence)
            {
                output.Add(line);
                continue;
            }

            if (!HtmlBlockStartPattern().IsMatch(line))
            {
                output.Add(ConvertInline(line));
                continue;
            }

            List<string> block = [];
            for (; i < lines.Length && lines[i].Trim().Length > 0 &&
                   !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal); i++)
                block.Add(lines[i]);
            i--;
            output.Add("");
            output.Add(ConvertBlock(string.Join("\n", block)));
            output.Add("");
        }

        return string.Join("\n", output);
    }

    // HTML 块里的换行只是空白（浏览器也这么排），段落由块级标签与 <br> 断开
    private static string ConvertBlock(string html)
    {
        string text = WhitespacePattern().Replace(html, " ");
        text = HtmlHeadingPattern().Replace(text,
            m => $"\n{new string('#', int.Parse(m.Groups[1].Value))} {m.Groups[2].Value.Trim()}\n");
        text = HtmlListItemPattern().Replace(text, "\n- ");
        text = BlockBreakPattern().Replace(text, "\n");
        text = HtmlLineBreakPattern().Replace(text, "\n");
        text = WebUtility.HtmlDecode(ConvertInline(text));
        IEnumerable<string> paragraphs = text.Split('\n')
            .Select(x => WhitespacePattern().Replace(x, " ").Trim())
            .Where(x => x.Length > 0 && x != "-");
        return string.Join("\n\n", paragraphs);
    }

    private static string ConvertInline(string html)
    {
        string text = HtmlImagePattern().Replace(html, "");
        text = HtmlLinkPattern().Replace(text, m =>
        {
            string label = WhitespacePattern().Replace(HtmlTagPattern().Replace(m.Groups[2].Value, ""), " ").Trim();
            return label.Length == 0 ? "" : $"[{label}]({m.Groups[1].Value})";
        });
        text = HtmlBoldPattern().Replace(text, "**");
        text = HtmlItalicPattern().Replace(text, "*");
        text = HtmlCodePattern().Replace(text, "`");
        text = HtmlLineBreakPattern().Replace(text, " ");
        return HtmlTagPattern().Replace(text, "");
    }

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex HtmlCommentPattern();

    [GeneratedRegex(@"^\s*</?[a-zA-Z][a-zA-Z0-9]*(\s[^>]*)?/?>")]
    private static partial Regex HtmlBlockStartPattern();

    [GeneratedRegex(@"<h([1-6])\b[^>]*>(.*?)</h\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex HtmlHeadingPattern();

    [GeneratedRegex(@"<li\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlListItemPattern();

    [GeneratedRegex(@"</?(p|div|li|ul|ol|table|tr|center|section|details|summary|blockquote|pre)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockBreakPattern();

    [GeneratedRegex(@"<img\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlImagePattern();

    [GeneratedRegex(@"<a\b[^>]*?href\s*=\s*[""']([^""']*)[""'][^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex HtmlLinkPattern();

    [GeneratedRegex(@"</?(strong|b)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlBoldPattern();

    [GeneratedRegex(@"</?(em|i)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlItalicPattern();

    [GeneratedRegex(@"</?code\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlCodePattern();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlLineBreakPattern();

    [GeneratedRegex(@"</?[a-zA-Z][a-zA-Z0-9]*(\s[^>]*)?/?>")]
    private static partial Regex HtmlTagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLinesPattern();
}
