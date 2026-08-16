/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

/// <summary>
/// 读网页正文的工具门面。怎么读交给 <see cref="FallbackPageReader"/>,这里只管截断与兜底话术。
/// </summary>
public static class WebFetchTool
{
    /// <summary>工具名。提示词里提到本工具时一律引用这个常量</summary>
    public const string ToolName = "WebFetch";

    private const int MaxChars = 6500;

    private static readonly FallbackPageReader Reader = new();

    public static AITool Create() => AIFunctionFactory.Create(
        FetchAsync, ToolName,
        "Read the main text of a web page, or the raw content of a plain-text/JSON/Markdown URL.");

    private static async Task<string> FetchAsync(
        [Description("Absolute URL of the page to read, including the scheme.")]
        string url,
        CancellationToken ct = default)
    {
        try
        {
            string text = await Reader.ReadAsync(url, ct);
            if (text.Length <= MaxChars) return text;

            // 说清截掉了多少:只丢一句 [Truncated],模型无从判断自己错过的是 5% 还是 95%
            return $"{text[..MaxChars]}\n\n---\n*[Truncated: showing first {MaxChars} of {text.Length} characters]*";
        }
        catch (OperationCanceledException)
        {
            return "[Timeout] Request was cancelled.";
        }
    }
}
