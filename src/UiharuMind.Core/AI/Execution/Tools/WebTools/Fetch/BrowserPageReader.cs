/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Execution.Tools.Browser;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

/// <summary>
/// 无头浏览器读正文：直连只拿到 JS 空壳的页面，在这里跑完脚本再抽。
/// 起浏览器要一两秒，所以排在便宜的读取器之后；没装 Chrome 时安静跳过。
/// </summary>
internal sealed class BrowserPageReader(IHtmlRenderer renderer) : IPageReader
{
    public string Name => "Headless browser";

    public bool CanRead(string url) =>
        renderer.IsAvailable && url.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
        !WebShared.IsLocalOrPrivateHost(url);

    public async Task<PageReadResult> ReadAsync(string url, CancellationToken ct)
    {
        (_, string html) = await renderer.RenderAsync(url, ct).ConfigureAwait(false);
        return PageReadResult.Ok(await DirectPageReader.ExtractTextAsync(html, ct).ConfigureAwait(false));
    }
}
