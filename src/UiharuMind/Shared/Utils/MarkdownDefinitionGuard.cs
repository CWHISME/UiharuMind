using System;
using System.Text;
using System.Text.RegularExpressions;

namespace UiharuMind.Shared.Utils;

/// <summary>
/// 挡住被 markdown 误吞的「链接引用定义」。
///
/// 一行以 <c>[名字]: 正文</c> 开头，CommonMark 就把它当链接引用定义（<c>[label]: destination</c>），
/// 整行不渲染——中文正文没有空格，整段都能被当成 destination 吞掉。群聊里模型常照着投递格式
/// 自加 <c>[自己名字]:</c>，结果是气泡空白、复制却有字。
///
/// 只转义 destination 不像地址的那些（真正的引用 <c>[1]: https://…</c> 照常生效），
/// 跳过围栏代码块（那里的字面值不能多出反斜杠）。只动渲染用的那份，原文一字不改
/// </summary>
public static partial class MarkdownDefinitionGuard
{
    /// <summary>
    /// 给会被误当成链接引用定义的行首 <c>[</c> 加转义
    /// </summary>
    /// <param name="markdown">markdown 原文</param>
    /// <returns>可安全渲染的文本；没有这种行时原样返回（同一实例）</returns>
    public static string Escape(string markdown)
    {
        if (string.IsNullOrEmpty(markdown) || !markdown.Contains("]:", StringComparison.Ordinal)) return markdown;

        StringBuilder? result = null;
        int lineStart = 0;
        bool inFence = false;
        while (lineStart <= markdown.Length)
        {
            int lineEnd = markdown.IndexOf('\n', lineStart);
            if (lineEnd < 0) lineEnd = markdown.Length;
            ReadOnlySpan<char> line = markdown.AsSpan(lineStart, lineEnd - lineStart);

            ReadOnlySpan<char> trimmed = line.TrimStart(' ');
            if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
            {
                inFence = !inFence;
            }
            else if (!inFence && DefinitionLikeLine().Match(line.ToString()) is { Success: true } match)
            {
                result ??= new StringBuilder(markdown.Length + 8).Append(markdown, 0, lineStart);
                int bracket = lineStart + match.Groups[1].Length;
                result.Append(markdown, lineStart, bracket - lineStart).Append('\\')
                    .Append(markdown, bracket, lineEnd - bracket);
                if (lineEnd < markdown.Length) result.Append('\n');
                lineStart = lineEnd + 1;
                continue;
            }

            result?.Append(markdown, lineStart, lineEnd - lineStart);
            if (lineEnd < markdown.Length) result?.Append('\n');
            lineStart = lineEnd + 1;
        }

        return result?.ToString() ?? markdown;
    }

    [GeneratedRegex(@"^( {0,3})\[[^\]\n]+\]:[ \t]*(?!<|[A-Za-z][A-Za-z0-9+.\-]*:|www\.|[/.#])\S")]
    private static partial Regex DefinitionLikeLine(); //行首至多三个空格 + [标签]: + 不像地址的 destination（地址 = <…>、带协议、www.、路径、锚点）
}
