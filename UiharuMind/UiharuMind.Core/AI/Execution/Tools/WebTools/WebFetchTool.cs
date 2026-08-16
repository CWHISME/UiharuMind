/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

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
        "Read the primary text of a public web page.");

    private static async Task<string> FetchAsync(string url, CancellationToken ct = default)
    {
        try
        {
            string text = await Reader.ReadAsync(url, ct);
            return text.Length > MaxChars ? $"{text[..MaxChars]}\n\n---\n*[Truncated]*" : text;
        }
        catch (OperationCanceledException)
        {
            return "[Timeout] Request was cancelled.";
        }
    }
}
