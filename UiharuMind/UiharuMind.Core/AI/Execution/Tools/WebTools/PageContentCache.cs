/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Concurrent;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

/// <summary>
/// 网页正文的短期缓存,兼做同 URL 并发去重。
///
/// 一轮对话里反复读同一个页面很常见(模型自己回头核对、几个子 agent 并行读同一份文档),
/// 每次都真去抓既慢又白烧 Firecrawl 额度。只缓存成功的结果——失败原因缓存下来会把一次
/// 偶发的 429 变成五分钟的"这页读不了"。
/// </summary>
internal sealed class PageContentCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    /// <summary>缓存条数上限。正文按 6500 字符量级算,这个数对内存无压力</summary>
    private const int Capacity = 64;

    private readonly record struct Entry(string Text, long ExpiresAtTick);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<PageFetchOutcome>>> _inFlight = new();

    /// <summary>
    /// 抓取结果
    /// </summary>
    /// <param name="Text">给模型看的文本(正文或错误说明)</param>
    /// <param name="Cacheable">是否值得缓存;失败结果一律不缓存</param>
    internal readonly record struct PageFetchOutcome(string Text, bool Cacheable);

    /// <summary>
    /// 取缓存,没有就抓一次。同一 URL 的并发调用共用同一次抓取。
    /// </summary>
    /// <param name="url">目标地址</param>
    /// <param name="fetch">真正的抓取动作</param>
    /// <param name="ct">调用方的取消令牌</param>
    /// <returns>正文或错误说明</returns>
    public async Task<string> GetOrFetchAsync(
        string url, Func<CancellationToken, Task<PageFetchOutcome>> fetch, CancellationToken ct)
    {
        if (TryGet(url, out string? cached))
        {
            Log.Debug($"[WebFetch] cache hit: {url} ({cached!.Length} chars)");
            return cached;
        }

        // 套一层 Lazy:抓取要到 .Value 才真正开始,也就必然晚于 GetOrAdd 的插入。
        // 直接塞 Task 的话,同步完成的抓取会在插入之前就把自己从在途表里删掉,
        // 那条目从此赖着不走,后来的请求全都拿到它——TTL 直接失效。
        Lazy<Task<PageFetchOutcome>> pending = _inFlight.GetOrAdd(
            url, key => new Lazy<Task<PageFetchOutcome>>(() => FetchAndStoreAsync(key, fetch)));

        // 抓取本身用 None 跑,调用方的 ct 只用来"不等了":否则先到的调用方一取消,
        // 正在等同一个 URL 的其他调用方会被连坐。抓取不会悬着——HttpClient 自带超时。
        PageFetchOutcome outcome = await pending.Value.WaitAsync(ct).ConfigureAwait(false);
        return outcome.Text;
    }

    private async Task<PageFetchOutcome> FetchAndStoreAsync(
        string url, Func<CancellationToken, Task<PageFetchOutcome>> fetch)
    {
        try
        {
            PageFetchOutcome outcome = await fetch(CancellationToken.None).ConfigureAwait(false);
            if (outcome.Cacheable) Store(url, outcome.Text);
            return outcome;
        }
        finally
        {
            _inFlight.TryRemove(url, out _);
        }
    }

    /// <summary>
    /// 取一条未过期的缓存
    /// </summary>
    /// <param name="url">目标地址</param>
    /// <param name="text">命中的正文</param>
    /// <returns>命中且未过期返回 true</returns>
    internal bool TryGet(string url, out string? text)
    {
        text = null;
        if (!_entries.TryGetValue(url, out Entry entry)) return false;

        if (entry.ExpiresAtTick <= Environment.TickCount64)
        {
            _entries.TryRemove(url, out _);
            return false;
        }

        text = entry.Text;
        return true;
    }

    internal void Store(string url, string text)
    {
        _entries[url] = new Entry(text, Environment.TickCount64 + (long)Ttl.TotalMilliseconds);
        if (_entries.Count <= Capacity) return;

        // 先清过期的;还超就丢最早到期的那条
        long now = Environment.TickCount64;
        foreach (KeyValuePair<string, Entry> pair in _entries)
        {
            if (pair.Value.ExpiresAtTick <= now) _entries.TryRemove(pair.Key, out _);
        }

        while (_entries.Count > Capacity)
        {
            string oldest = _entries.MinBy(pair => pair.Value.ExpiresAtTick).Key;
            _entries.TryRemove(oldest, out _);
        }
    }
}
