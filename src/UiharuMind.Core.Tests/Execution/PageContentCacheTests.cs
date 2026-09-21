using UiharuMind.Core.AI.Execution.Tools.WebTools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 正文缓存与同 URL 并发去重。钉死三条:成功才缓存、并发只抓一次、
/// 一个调用方取消不能连坐其它等待者。
/// </summary>
public class PageContentCacheTests
{
    private const string Url = "https://cache.example/doc";

    private static PageContentCache.PageFetchOutcome Ok(string text) => new(text, Cacheable: true);

    [Fact]
    public async Task SuccessfulResult_IsServedFromCache()
    {
        PageContentCache cache = new();
        int calls = 0;

        Task<PageContentCache.PageFetchOutcome> Fetch(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(Ok("body"));
        }

        Assert.Equal("body", await cache.GetOrFetchAsync(Url, Fetch, CancellationToken.None));
        Assert.Equal("body", await cache.GetOrFetchAsync(Url, Fetch, CancellationToken.None));
        Assert.Equal(1, calls);
    }

    /// <summary>失败结果缓存下来,会把一次偶发的 429 变成五分钟的"这页读不了"</summary>
    [Fact]
    public async Task FailedResult_IsNotCached()
    {
        PageContentCache cache = new();
        int calls = 0;

        Task<PageContentCache.PageFetchOutcome> Fetch(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new PageContentCache.PageFetchOutcome("[Error] nope", Cacheable: false));
        }

        await cache.GetOrFetchAsync(Url, Fetch, CancellationToken.None);
        await cache.GetOrFetchAsync(Url, Fetch, CancellationToken.None);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ConcurrentCallsForSameUrl_FetchOnlyOnce()
    {
        PageContentCache cache = new();
        TaskCompletionSource gate = new();
        int calls = 0;

        async Task<PageContentCache.PageFetchOutcome> Fetch(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            await gate.Task;
            return Ok("body");
        }

        Task<string> first = cache.GetOrFetchAsync(Url, Fetch, CancellationToken.None);
        Task<string> second = cache.GetOrFetchAsync(Url, Fetch, CancellationToken.None);
        gate.SetResult();

        Assert.Equal("body", await first);
        Assert.Equal("body", await second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task OneCallerCancelling_DoesNotAffectOthers()
    {
        PageContentCache cache = new();
        TaskCompletionSource gate = new();
        using CancellationTokenSource cts = new();

        async Task<PageContentCache.PageFetchOutcome> Fetch(CancellationToken _)
        {
            await gate.Task;
            return Ok("body");
        }

        Task<string> cancelled = cache.GetOrFetchAsync(Url, Fetch, cts.Token);
        Task<string> survivor = cache.GetOrFetchAsync(Url, Fetch, CancellationToken.None);

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

        gate.SetResult();
        Assert.Equal("body", await survivor);
    }

    [Fact]
    public void StoredEntry_IsServed_UnknownUrlIsNot()
    {
        PageContentCache cache = new();
        cache.Store(Url, "body");

        Assert.True(cache.TryGet(Url, out string? hit));
        Assert.Equal("body", hit);
        Assert.False(cache.TryGet("https://cache.example/other", out _));
    }
}
