using System.Collections.Concurrent;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Status;

/// <summary>
/// 供应商状态的取数服务：缓存、单飞、失败退避全在这<b>一处</b>，界面只管「什么时候问、怎么显示」。
///
/// 三条容易写错的地方（都在单测里钉着）：
/// ① <b>单飞是按 key 的</b>。缓存的键是 <c>(configType, apiBase, keyHash)</c>，同一时刻可能有多个 key
///    的在途请求；拿一个字段存「正在取的那次」会把甲的结果返回给乙。
/// ② <b>失败留旧值</b>：顶栏是背景信息，网络抖一下不该让读数消失；连失败按 15s → 翻倍 → 5min 退避。
/// ③ <b>无 Source 不建缓存项</b>：没接入的供应商连字典项都不该有，否则每切一次模型就多一个空壳。
/// </summary>
public sealed class ProviderStatusService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);
    private const int MaxEntries = 64; //超过就清一遍久未触及的项（用户可能建很多把 key 的模型）
    private static readonly TimeSpan EntryIdleLimit = TimeSpan.FromHours(1);

    private readonly IProviderStatusRegistry _registry;
    private readonly Func<DateTimeOffset> _now;
    private readonly ConcurrentDictionary<ProviderStatusCacheKey, Entry> _entries = new();

    /// <summary>全应用共用的一份</summary>
    public static ProviderStatusService Shared { get; } = new(ProviderStatusRegistry.Instance);

    /// <summary>
    /// 造一份取数服务。<b>只在需要换注册表或换时钟时用</b>：应用里一律走
    /// <see cref="Shared"/>——缓存按 key 共享，各造各的会让同一把 key 被重复请求
    /// </summary>
    /// <param name="registry">注册表</param>
    /// <param name="now">时钟；不给就用系统时钟</param>
    public ProviderStatusService(IProviderStatusRegistry registry, Func<DateTimeOffset>? now = null)
    {
        _registry = registry;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    /// <summary>
    /// 取一次状态
    /// </summary>
    /// <param name="context">取数输入</param>
    /// <param name="force">跳过 TTL（仍受失败退避约束）</param>
    /// <param name="ct">取消（只取消本次等待，不取消已在途的那次请求）</param>
    /// <returns>快照；这家没有状态接口时返回 null</returns>
    public async Task<ProviderStatusSnapshot?> GetAsync(ProviderStatusContext context, bool force = false,
        CancellationToken ct = default)
    {
        IProviderStatusSource? source = _registry.For(context.ConfigType)?.Source;
        if (source == null) return null;

        ProviderStatusCacheKey key = new(context.ConfigType, context.ApiBaseUrl,
            ProviderStatusCacheKey.HashKey(context.ApiKey));
        Entry entry = _entries.GetOrAdd(key, static _ => new Entry());

        Task<ProviderStatusSnapshot?> fetch;
        lock (entry.Gate)
        {
            DateTimeOffset now = _now();
            entry.LastTouchedAt = now;

            // 失败退避优先于一切，force 也不例外：否则「手动刷新」一次就把退避打穿
            if (entry.NextAttemptAt > now) return entry.LastGood;
            if (!force && entry.LastGood != null && now - entry.LastGoodAt < Ttl) return entry.LastGood;

            if (entry.InFlight is { IsCompleted: false } running)
            {
                fetch = running;
            }
            else
            {
                entry.InFlight = fetch = FetchCoreAsync(source, entry, context);
            }
        }

        PruneIfCrowded();
        return await fetch.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>最后一次成功取到的快照（无则 null），给「切换目标时先顶一下」用</summary>
    /// <param name="context">取数输入</param>
    /// <returns>缓存里的快照</returns>
    public ProviderStatusSnapshot? Peek(ProviderStatusContext context)
    {
        ProviderStatusCacheKey key = new(context.ConfigType, context.ApiBaseUrl,
            ProviderStatusCacheKey.HashKey(context.ApiKey));
        return _entries.TryGetValue(key, out Entry? entry) ? entry.LastGood : null;
    }

    private async Task<ProviderStatusSnapshot?> FetchCoreAsync(IProviderStatusSource source, Entry entry,
        ProviderStatusContext context)
    {
        try
        {
            ProviderStatusSnapshot? snapshot = await source.FetchAsync(context, CancellationToken.None)
                .ConfigureAwait(false);
            lock (entry.Gate)
            {
                if (snapshot != null)
                {
                    entry.LastGood = snapshot;
                    entry.LastGoodAt = snapshot.FetchedAt;
                }

                entry.Failures = 0;
                entry.NextAttemptAt = DateTimeOffset.MinValue;
                return snapshot ?? entry.LastGood;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 日志只用 context.ToString()（不含密钥），异常本身可能带请求头，故只记类型与消息
            Log.Debug($"[ProviderStatus] fetch failed: {context} · {ex.GetType().Name}: {ex.Message}");
            lock (entry.Gate)
            {
                entry.Failures++;
                int steps = Math.Min(entry.Failures - 1, 8);
                long ticks = Math.Min(MaxBackoff.Ticks, MinBackoff.Ticks << steps);
                entry.NextAttemptAt = _now() + TimeSpan.FromTicks(ticks);
                return entry.LastGood;
            }
        }
    }

    private void PruneIfCrowded()
    {
        if (_entries.Count <= MaxEntries) return;

        DateTimeOffset now = _now();
        foreach (KeyValuePair<ProviderStatusCacheKey, Entry> pair in _entries)
        {
            lock (pair.Value.Gate)
            {
                if (now - pair.Value.LastTouchedAt > EntryIdleLimit) _entries.TryRemove(pair.Key, out _);
            }
        }
    }

    private sealed class Entry
    {
        public readonly object Gate = new();
        public ProviderStatusSnapshot? LastGood;
        public DateTimeOffset LastGoodAt;
        public DateTimeOffset NextAttemptAt = DateTimeOffset.MinValue;
        public DateTimeOffset LastTouchedAt;
        public int Failures;
        public Task<ProviderStatusSnapshot?>? InFlight;
    }
}
