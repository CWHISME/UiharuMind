/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

/// <summary>
/// 自己下 HTML 扒 DOM 取正文。不依赖任何第三方服务,所以放在链尾兜底;
/// 代价是 JS 渲染页与非 HTML 内容读不出东西。
/// </summary>
internal sealed partial class HtmlPageReader : IPageReader
{
    private const long ResponseSizeCap = 512_000;

    private static readonly string[] NoiseTags =
        ["script", "style", "noscript", "iframe", "svg", "nav", "footer", "aside", "header"];

    public string Name => "DirectHtml";

    public async Task<PageReadResult> ReadAsync(string url, CancellationToken ct)
    {
        try
        {
            using HttpResponseMessage resp = await WebShared.Http.SendAsync(
                WebShared.CreateFetchRequest(url),
                HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode) return PageReadResult.Fail($"HTTP {(int)resp.StatusCode}");

            if (resp.Content.Headers.ContentType?.MediaType is not "text/html")
                return PageReadResult.Fail($"not an HTML page: {resp.Content.Headers.ContentType?.MediaType}");

            BoundedStream stream = new(await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false),
                ResponseSizeCap);
            // 每次请求创建新的 Context，确保线程安全（多 agent 同时调的话）
            using IBrowsingContext context = BrowsingContext.New(Configuration.Default);
            using IDocument doc = await context.OpenAsync(v => v.Content(stream), ct).ConfigureAwait(false);

            StripNoise(doc);
            return PageReadResult.Ok(Extract(doc));
        }
        catch (BoundedStream.LimitExceeded)
        {
            return PageReadResult.Fail("response too large");
        }
    }

    // ── DOM 清理 ──────────────────────────────────────────────
    private static void StripNoise(IDocument doc)
    {
        //并选择器，一次性移除，减少 DOM 遍历次数
        string selector = string.Join(",", NoiseTags);
        foreach (IElement node in doc.QuerySelectorAll(selector).ToList())
            node.Remove();

        foreach (IElement el in doc.QuerySelectorAll("*")
                     .Where(e => e.TagName is not ("IMG" or "BR" or "HR")
                                 && string.IsNullOrWhiteSpace(e.TextContent))
                     .ToList())
            el.Remove();
    }

    // ── 正文提取 ──────────────────────────────────────────────
    private static string Extract(IDocument doc)
    {
        IElement? root = doc.QuerySelector("article, main, [role='main']")
                         ?? doc.QuerySelectorAll("section, div")
                             .MaxBy(e => e.QuerySelectorAll("p").Count);

        string raw = root?.TextContent ?? doc.Body?.TextContent ?? "";
        return MultiNewlineRegex().Replace(raw.Trim(), "\n\n");
    }

    // ── 截断流 ────────────────────────────────────────────────
    private sealed class BoundedStream(Stream inner, long cap) : Stream
    {
        public sealed class LimitExceeded : Exception;

        private long _read;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = inner.Read(buffer, offset, count);
            _read += n;
            if (_read > cap) throw new LimitExceeded();
            return n;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            int n = await inner.ReadAsync(buffer, ct);
            _read += n;
            if (_read > cap) throw new LimitExceeded();
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [GeneratedRegex(@"\n{3,}", RegexOptions.Compiled)]
    private static partial Regex MultiNewlineRegex();
}
