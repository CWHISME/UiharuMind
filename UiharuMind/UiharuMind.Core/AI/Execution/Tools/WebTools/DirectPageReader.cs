/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

/// <summary>
/// 自己下正文,不依赖任何第三方服务,所以放在链尾兜底。HTML 扒 DOM,纯文本类原样取回
/// (raw.githubusercontent、JSON 接口、.md/.txt 都属这类,交给 DOM 解析器只会把内容毁掉)。
/// 代价是 JS 渲染页与二进制内容(PDF 等)读不出东西——那是链首 Firecrawl 的活。
/// </summary>
internal sealed partial class DirectPageReader : IPageReader
{
    private const long ResponseSizeCap = 512_000;

    private static readonly string[] NoiseTags =
        ["script", "style", "noscript", "iframe", "svg", "nav", "footer", "aside", "header"];

    /// <summary>需要走 DOM 解析的类型</summary>
    private static readonly string[] MarkupTypes = ["text/html", "application/xhtml+xml"];

    /// <summary>text/* 之外还该当纯文本看的类型;这些拿去解析 DOM 会被吃掉内容</summary>
    private static readonly string[] PlainTextTypes =
    [
        "application/json", "application/ld+json", "application/xml", "application/yaml",
        "application/x-yaml", "application/javascript", "application/x-ndjson"
    ];

    public string Name => "Direct";

    public async Task<PageReadResult> ReadAsync(string url, CancellationToken ct)
    {
        using HttpResponseMessage resp = await WebShared.Http.SendAsync(
            WebShared.CreateFetchRequest(url),
            HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode) return PageReadResult.Fail($"HTTP {(int)resp.StatusCode}");

        string mediaType = resp.Content.Headers.ContentType?.MediaType ?? "";
        Stream body = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        if (IsMarkup(mediaType)) return await ReadMarkupAsync(body, ct).ConfigureAwait(false);

        if (IsPlainText(mediaType))
        {
            Encoding encoding = ResolveEncoding(resp.Content.Headers.ContentType?.CharSet);
            string text = await ReadCappedTextAsync(body, encoding, ct).ConfigureAwait(false);
            return text.Length > 0 ? PageReadResult.Exact(text) : PageReadResult.Fail("empty response body");
        }

        return PageReadResult.Fail($"unsupported content type: {(mediaType.Length > 0 ? mediaType : "unknown")}");
    }

    /// <summary>是否按标记语言解析。类型缺失时也按 HTML 试——多数没声明的其实就是网页</summary>
    private static bool IsMarkup(string mediaType) =>
        mediaType.Length == 0 || MarkupTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase);

    private static bool IsPlainText(string mediaType) =>
        mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
        PlainTextTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase);

    private static Encoding ResolveEncoding(string? charSet)
    {
        if (string.IsNullOrWhiteSpace(charSet)) return Encoding.UTF8;
        try
        {
            return Encoding.GetEncoding(charSet.Trim('"'));
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }

    private static async Task<PageReadResult> ReadMarkupAsync(Stream body, CancellationToken ct)
    {
        try
        {
            BoundedStream stream = new(body, ResponseSizeCap);
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

    /// <summary>
    /// 读纯文本,读满上限就停。这里刻意不像 HTML 那样超限即失败:文本截一段仍然可用,
    /// 何况工具层本来就要按字符数再截一次。
    /// </summary>
    private static async Task<string> ReadCappedTextAsync(Stream body, Encoding encoding, CancellationToken ct)
    {
        byte[] buffer = new byte[ResponseSizeCap];
        int filled = 0;
        while (filled < buffer.Length)
        {
            int n = await body.ReadAsync(buffer.AsMemory(filled), ct).ConfigureAwait(false);
            if (n == 0) break;
            filled += n;
        }

        return encoding.GetString(buffer, 0, filled).Trim();
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
