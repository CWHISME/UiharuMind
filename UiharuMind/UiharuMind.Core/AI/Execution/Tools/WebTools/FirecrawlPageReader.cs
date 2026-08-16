/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.Json;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

/// <summary>
/// Firecrawl 抓正文,直接拿到 markdown。无 key 也能用(见 <see cref="FirecrawlClient"/>),
/// 且能处理 JS 渲染页与 PDF——这两类正是自己扒 DOM 读不出东西的场景,所以排在链首。
/// </summary>
internal sealed class FirecrawlPageReader : IPageReader
{
    private const int ScrapeTimeoutMs = 12_000; //服务端超时留在 WebShared.Http 的 15s 之内,好让失败以 JSON 回来而不是断线

    /// <summary>
    /// 服务端缓存可接受的最大年龄。Firecrawl 默认 48 小时,对"查最新版本/看今天的公告"
    /// 这类问题会悄悄给出两天前的页面且不报错,所以收到 1 小时——仍能吃到重复抓取的便宜。
    /// </summary>
    private const int CacheMaxAgeMs = 3_600_000;

    public string Name => "Firecrawl";

    /// <summary>
    /// 内网与本机地址一概不受理:Firecrawl 在它自己的机器上解析,既取不到你的内网页面,
    /// 还把地址送了出去。这类地址直接留给链尾的直连读取器。
    /// </summary>
    /// <param name="url">目标地址</param>
    /// <returns>公网地址返回 true</returns>
    public bool CanRead(string url) => !WebShared.IsLocalOrPrivateHost(url);

    public async Task<PageReadResult> ReadAsync(string url, CancellationToken ct)
    {
        string json = await FirecrawlClient.PostAsync(
            "scrape",
            new
            {
                url,
                formats = new[] { "markdown" },
                onlyMainContent = true,
                timeout = ScrapeTimeoutMs,
                maxAge = CacheMaxAgeMs
            },
            ct).ConfigureAwait(false);
        return Parse(json);
    }

    /// <summary>
    /// 解析 Firecrawl 响应({"data":{"markdown":"..."}})
    /// </summary>
    /// <param name="json">响应 JSON</param>
    /// <returns>正文或失败原因</returns>
    internal static PageReadResult Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out JsonElement data) ||
            data.ValueKind != JsonValueKind.Object)
        {
            return PageReadResult.Fail("response has no data");
        }

        string markdown = WebShared.GetString(data, "markdown");
        return markdown.Length > 0 ? PageReadResult.Ok(markdown) : PageReadResult.Fail("empty markdown");
    }
}
