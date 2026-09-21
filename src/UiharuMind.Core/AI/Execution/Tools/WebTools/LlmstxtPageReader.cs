/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

/// <summary>
/// 读同源 <c>/llms.txt</c>（llmstxt.org 倡议的 LLM 友好入口）的专用读取器。
/// <para>
/// 独立成环而不是塞进 <see cref="DirectPageReader"/>：它是"同源变体入口"而非"URL 正文"。
/// 按 llmstxt.org 提案，agent 本应先读 llms.txt 再 follow 链接，故排在直连读取器之前：
/// 它对 SPA 空壳页是唯一不用渲染 JS 就能拿到的指路，对文档站直接给出 .md 版本链接；
/// 站点没有 llms.txt 时 404 快速落空，随即由直连读取器兜底。
/// </para>
/// </summary>
internal sealed class LlmstxtPageReader : IPageReader
{
    private const int ResponseSizeCap = 4 * 1024 * 1024;

    public string Name => "llms.txt";

    /// <summary>只在 http/https 上试;无法解析出源的地址直接不受理</summary>
    public bool CanRead(string url) => BuildLlmstxtUrl(url) != null;

    public async Task<PageReadResult> ReadAsync(string url, CancellationToken ct)
    {
        string llmsUrl = BuildLlmstxtUrl(url)!;

        using HttpResponseMessage resp = await WebShared.Http.SendAsync(
            WebShared.CreateFetchRequest(llmsUrl),
            HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return PageReadResult.Fail($"HTTP {(int)resp.StatusCode}");

        Stream body = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        string text = await ReadCappedTextAsync(body, ct).ConfigureAwait(false);
        return text.Length > 0
            ? PageReadResult.Ok($"[Loaded from {llmsUrl} — the site's LLM-friendly entry point]\n\n{text}")
            : PageReadResult.Fail("empty llms.txt");
    }

    /// <summary>由目标 URL 拼出同源 <c>/llms.txt</c> 地址;非 http/https 或解析失败返回 null</summary>
    internal static string? BuildLlmstxtUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return null;
        if (uri.Scheme is not ("http" or "https")) return null;
        return $"{uri.Scheme}://{uri.Authority}/llms.txt";
    }

    /// <summary>读纯文本,读满上限即停(超限截断可用,与 Direct 的纯文本分支同口径)</summary>
    private static async Task<string> ReadCappedTextAsync(Stream body, CancellationToken ct)
    {
        byte[] buffer = new byte[ResponseSizeCap];
        int filled = 0;
        while (filled < buffer.Length)
        {
            int n = await body.ReadAsync(buffer.AsMemory(filled), ct).ConfigureAwait(false);
            if (n == 0) break;
            filled += n;
        }

        return Encoding.UTF8.GetString(buffer, 0, filled).Trim();
    }
}
