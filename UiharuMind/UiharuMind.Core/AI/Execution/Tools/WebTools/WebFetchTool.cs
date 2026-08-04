using System.ComponentModel;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

public static partial class WebFetchTool
{

    /// <summary>工具名。提示词里提到本工具时一律引用这个常量</summary>
    public const string ToolName = "WebFetch";

    private static readonly string[] NoiseTags =
        ["script", "style", "noscript", "iframe", "svg", "nav", "footer", "aside", "header"];

    public static AITool Create() => AIFunctionFactory.Create(
        FetchAsync, ToolName,
        "Read the primary text of a public web page.");

    private static async Task<string> FetchAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var resp = await WebShared.Http.SendAsync(
                WebShared.CreateFetchRequest(url),
                HttpCompletionOption.ResponseHeadersRead, ct);

            if (!resp.IsSuccessStatusCode)
                return $"[Error] HTTP {(int)resp.StatusCode}";

            if (resp.Content.Headers.ContentType?.MediaType is not "text/html")
                return $"[Skip] Not an HTML page:{resp.Content.Headers.ContentType?.MediaType}";

            var stream = new BoundedStream(await resp.Content.ReadAsStreamAsync(ct), 512_000);
            // 每次请求创建新的 Context，确保线程安全（多 agent 同时调的话）
            using var context = BrowsingContext.New(Configuration.Default);
            using var doc = await context.OpenAsync(v => v.Content(stream), ct);

            StripNoise(doc);
            return Truncate(Extract(doc));
        }
        catch (BoundedStream.LimitExceeded)
        {
            return "[Blocked] Response too large.";
        }
        catch (OperationCanceledException)
        {
            return "[Timeout] Request was cancelled.";
        }
    }

    // ── DOM 清理 ──────────────────────────────────────────────
    private static void StripNoise(IDocument doc)
    {
        //并选择器，一次性移除，减少 DOM 遍历次数
        var selector = string.Join(",", NoiseTags);
        foreach (var node in doc.QuerySelectorAll(selector).ToList())
            node.Remove();

        foreach (var el in doc.QuerySelectorAll("*")
                     .Where(e => e.TagName is not ("IMG" or "BR" or "HR")
                                 && string.IsNullOrWhiteSpace(e.TextContent))
                     .ToList())
            el.Remove();
    }

    // ── 正文提取 ──────────────────────────────────────────────
    private static string Extract(IDocument doc)
    {
        var root = doc.QuerySelector("article, main, [role='main']")
                   ?? doc.QuerySelectorAll("section, div")
                         .MaxBy(e => e.QuerySelectorAll("p").Count);

        var raw = root?.TextContent ?? doc.Body?.TextContent ?? "";
        return MultiNewlineRegex().Replace(raw.Trim(), "\n\n");
    }

    private static string Truncate(string text) => text.Length switch
    {
        < 150  => "[Empty] Likely JS-rendered; no readable text.",
        > 6500 => $"{text[..6500]}\n\n---\n*[Truncated]*",
        _      => text
    };

    // ── 截断流 ────────────────────────────────────────────────
    private sealed class BoundedStream(Stream inner, long cap) : Stream
    {
        public sealed class LimitExceeded : Exception;

        private long _read;

        public override bool CanRead  => true;
        public override bool CanSeek  => false;
        public override bool CanWrite => false;
        public override long Length   => throw new NotSupportedException();
        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            _read += n;
            if (_read > cap) throw new LimitExceeded();
            return n;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var n = await inner.ReadAsync(buffer, ct);
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