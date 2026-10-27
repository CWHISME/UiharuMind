using UiharuMind.Core.AI.Status;
using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.Tests.AI.Status;

/// <summary>
/// 取数服务的四条口径：按 key 缓存、按 key 单飞、TTL、失败留旧值 + 退避。
/// 这些行为都在服务里、与供应商无关，所以这里用假 Source 打，不碰网络。
/// </summary>
public class ProviderStatusServiceTests
{
    private static readonly Type CcType = typeof(RemoteCommandCodeModelConfig);

    private DateTimeOffset _now = new(2026, 10, 27, 10, 0, 0, TimeSpan.Zero);

    private ProviderStatusService CreateService(IProviderStatusSource source) =>
        new(new FakeRegistry(new ProviderStatusRegistration(source, null)), () => _now);

    private static ProviderStatusContext Context(string key = "key-a") =>
        new(CcType, "https://api.commandcode.ai", key, "deepseek-v4-flash", "DeepSeek V4 Flash");

    [Fact]
    public async Task ConcurrentCalls_ShareOneFetch()
    {
        FakeSource source = new(() => _now) { Gate = new TaskCompletionSource() };
        ProviderStatusService service = CreateService(source);

        Task<ProviderStatusSnapshot?>[] calls =
            [.. Enumerable.Range(0, 5).Select(_ => service.GetAsync(Context()))];

        Assert.Equal(1, source.Calls);

        source.Gate.SetResult();
        ProviderStatusSnapshot?[] results = await Task.WhenAll(calls);

        Assert.All(results, snapshot => Assert.NotNull(snapshot));
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task WithinTtl_ReturnsCache()
    {
        FakeSource source = new(() => _now);
        ProviderStatusService service = CreateService(source);

        await service.GetAsync(Context());
        _now = _now.AddSeconds(30);
        await service.GetAsync(Context());

        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task AfterTtl_FetchesAgain()
    {
        FakeSource source = new(() => _now);
        ProviderStatusService service = CreateService(source);

        await service.GetAsync(Context());
        _now = _now.AddSeconds(61);
        await service.GetAsync(Context());

        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task Force_SkipsTtl()
    {
        FakeSource source = new(() => _now);
        ProviderStatusService service = CreateService(source);

        await service.GetAsync(Context());
        await service.GetAsync(Context(), force: true);

        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task DifferentKeys_DoNotShareCache()
    {
        FakeSource source = new(() => _now);
        ProviderStatusService service = CreateService(source);

        await service.GetAsync(Context("key-a"));
        await service.GetAsync(Context("key-b"));

        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task SecondFailure_ReturnsLastGoodSnapshot()
    {
        FakeSource source = new(() => _now);
        ProviderStatusService service = CreateService(source);

        ProviderStatusSnapshot? first = await service.GetAsync(Context());
        Assert.NotNull(first);

        source.Fail = new HttpRequestException("boom");
        _now = _now.AddSeconds(61);
        ProviderStatusSnapshot? second = await service.GetAsync(Context());

        Assert.Same(first, second);
    }

    [Fact]
    public async Task Failure_BacksOffBeforeTryingAgain()
    {
        FakeSource source = new(() => _now) { Fail = new HttpRequestException("boom") };
        ProviderStatusService service = CreateService(source);

        await service.GetAsync(Context());
        Assert.Equal(1, source.Calls);

        // 退避期内：即使强制刷新也不发请求
        _now = _now.AddSeconds(5);
        await service.GetAsync(Context(), force: true);
        Assert.Equal(1, source.Calls);

        // 过了第一档退避（15s）才再试
        _now = _now.AddSeconds(15);
        await service.GetAsync(Context(), force: true);
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task NoSource_ReturnsNull_AndCachesNothing()
    {
        ProviderStatusService service = new(new FakeRegistry(null), () => _now);

        ProviderStatusSnapshot? snapshot = await service.GetAsync(Context());

        Assert.Null(snapshot);
        Assert.Null(service.Peek(Context()));
    }

    [Fact]
    public async Task Peek_SeesCachedSnapshot()
    {
        FakeSource source = new(() => _now);
        ProviderStatusService service = CreateService(source);

        await service.GetAsync(Context());

        Assert.NotNull(service.Peek(Context()));
    }

    private sealed class FakeRegistry(ProviderStatusRegistration? registration) : IProviderStatusRegistry
    {
        public ProviderStatusRegistration? For(Type configType) =>
            configType == CcType ? registration : null;
    }

    private sealed class FakeSource(Func<DateTimeOffset> now) : IProviderStatusSource
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public TaskCompletionSource? Gate { get; init; }

        public Exception? Fail { get; set; }

        public async Task<ProviderStatusSnapshot?> FetchAsync(ProviderStatusContext context, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            if (Gate != null) await Gate.Task.ConfigureAwait(false);
            if (Fail != null) throw Fail;
            return new ProviderStatusSnapshot("Command Code", [], null, [], now());
        }
    }
}
