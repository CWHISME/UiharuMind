using System.Net;
using System.Text.RegularExpressions;

namespace UiharuMind.Core.AI.Models.Sources;

/// <summary>
/// 仓库说明（README）：拉取原文，整理成适合直接渲染的 markdown
/// </summary>
public static partial class ModelReadme
{
    /// <summary>
    /// 拉一份说明原文；404 视为没有
    /// </summary>
    /// <param name="httpClient">HTTP 客户端</param>
    /// <param name="url">README 地址</param>
    /// <param name="headers">额外请求头（如令牌）</param>
    /// <param name="cancellationToken">取消</param>
    /// <returns>原文；没有为 null</returns>
    public static async Task<string?> FetchAsync(HttpClient httpClient, string url,
        IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        foreach ((string key, string value) in headers ?? new Dictionary<string, string>())
            request.Headers.TryAddWithoutValidation(key, value);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 整理成渲染器吃得下的 markdown：去掉开头的 YAML 元数据与 HTML 注释，
    /// 代码块以外的 HTML 化成 markdown（链接、标题、粗斜体保留，图片徽标与排版标签丢掉）
    /// </summary>
    /// <param name="markdown">原文</param>
    /// <returns>整理后的 markdown</returns>
    public static string Clean(string markdown)
    {
        string text = markdown.Replace("\r\n", "\n");
        text = FrontMatterPattern().Replace(text, "", 1);
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

    private static string ConvertBlock(string html)
    {
        string text = HtmlHeadingPattern().Replace(html,
            m => $"\n{new string('#', int.Parse(m.Groups[1].Value))} {m.Groups[2].Value.Trim()}\n");
        text = BlockBreakPattern().Replace(text, "\n");
        text = WebUtility.HtmlDecode(ConvertInline(text));
        IEnumerable<string> paragraphs = text.Split('\n')
            .Select(x => WhitespacePattern().Replace(x, " ").Trim())
            .Where(x => x.Length > 0);
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
        text = HtmlLineBreakPattern().Replace(text, " ");
        return HtmlTagPattern().Replace(text, "");
    }

    [GeneratedRegex(@"\A﻿?---\n.*?\n---[ \t]*(\n|\z)", RegexOptions.Singleline)]
    private static partial Regex FrontMatterPattern();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex HtmlCommentPattern();

    [GeneratedRegex(@"^\s*</?[a-zA-Z][a-zA-Z0-9]*(\s[^>]*)?/?>")]
    private static partial Regex HtmlBlockStartPattern();

    [GeneratedRegex(@"<h([1-6])\b[^>]*>(.*?)</h\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex HtmlHeadingPattern();

    [GeneratedRegex(@"</?(p|div|li|ul|ol|table|tr|center|section|details|summary)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockBreakPattern();

    [GeneratedRegex(@"<img\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlImagePattern();

    [GeneratedRegex(@"<a\b[^>]*?href\s*=\s*[""']([^""']*)[""'][^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex HtmlLinkPattern();

    [GeneratedRegex(@"</?(strong|b)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlBoldPattern();

    [GeneratedRegex(@"</?(em|i)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlItalicPattern();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlLineBreakPattern();

    [GeneratedRegex(@"</?[a-zA-Z][a-zA-Z0-9]*(\s[^>]*)?/?>")]
    private static partial Regex HtmlTagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLinesPattern();
}
